using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Application.Authorization;
using WashFlow.Application.Invitations;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Email;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

/// <summary>Real endpoint pipeline + isolated PostgreSQL; no mocked auth or invitation stores.</summary>
[Trait("Category", "PostgreSQL")]
public sealed class InvitationLifecycleHttpPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private const string Password = "Synthetic test password 123!";
    private static string Email() => $"{Guid.NewGuid():N}@example.test";
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root) => root.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.RemoveAll<WashFlowDbContext>();
        s.AddScoped(_ => Db());
    }));
    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response) { Assert.Equal(expected, response.StatusCode); }
        await Task.CompletedTask;
    }
    private static async Task<string> Mail(WebApplicationFactory<Program> api, string email, string purpose)
    {
        var mailbox = api.Services.GetRequiredService<DevelopmentEmailSender>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (mailbox.TryTake(email, purpose, out var message)) return message!.Token;
            await Task.Delay(10, timeout.Token);
        }
    }
    private static async Task Register(WebApplicationFactory<Program> api, HttpClient client, string email, bool owner)
    {
        await Status(await client.PostAsJsonAsync(owner ? "/api/v1/auth/businesses/register" : "/api/v1/auth/customers/register",
            new { fullName = "Test person", ownerFullName = "Test owner", businessName = "HTTP test business", email, password = Password }), HttpStatusCode.Accepted);
        var verification = await Mail(api, email, "email-verification");
        await Status(await client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = verification }), HttpStatusCode.OK);
    }
    private static async Task<TokenResponse> Login(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens;
    }
    private static async Task<UserContexts> Contexts(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/contexts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserContexts>())!;
    }
    private static string Path(Guid business) => $"/api/v1/businesses/{business}/employee-invitations";
    private static async Task<InvitationView> Invite(HttpClient client, Guid business, string email)
    {
        using var response = await client.PostAsJsonAsync(Path(business), new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationView>())!;
    }

    [PostgresFact]
    public async Task OwnerAndExistingRecipientCompleteRevokeResendAcceptRefreshAndLogoutThroughHttp()
    {
        using var root = new ApiFactory(); using var api = Api(root);
        using var owner = api.CreateClient(); using var recipient = api.CreateClient();
        var ownerEmail = Email(); var email = Email();
        await Register(api, owner, ownerEmail, true); await Login(owner, ownerEmail);
        var business = Assert.Single((await Contexts(owner)).BusinessContexts).BusinessId;
        await Register(api, recipient, email, false); var login = await Login(recipient, email);
        var invitation = await Invite(owner, business, email);
        var first = await Mail(api, email, "employee-invitation");
        var rows = (await owner.GetFromJsonAsync<List<InvitationView>>(Path(business)))!;
        var listed = Assert.Single(rows); Assert.Null(listed.AcceptedAt); Assert.Null(listed.RevokedAt); Assert.True(listed.ExpiresAt > DateTimeOffset.UtcNow);
        await Status(await owner.PostAsync($"{Path(business)}/{invitation.Id}/revoke", null), HttpStatusCode.NoContent);
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = first }), HttpStatusCode.BadRequest);
        invitation = await Invite(owner, business, email);
        var old = await Mail(api, email, "employee-invitation");
        using var resend = await owner.PostAsync($"{Path(business)}/{invitation.Id}/resend", null);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var current = await Mail(api, email, "employee-invitation");
        Assert.True(old != current); // Never include credential values in assertion diagnostics.
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = old }), HttpStatusCode.BadRequest);
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = current }), HttpStatusCode.NoContent);
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = current }), HttpStatusCode.BadRequest);
        var contexts = await Contexts(recipient);
        Assert.NotNull(contexts.CustomerContext); // Existing customer profile is preserved.
        var membership = Assert.Single(contexts.BusinessContexts);
        Assert.Equal(business, membership.BusinessId); Assert.Equal(new[] { "Employee" }, membership.Roles);
        await Status(await recipient.GetAsync(Path(business)), HttpStatusCode.Forbidden);
        await Status(await owner.PostAsJsonAsync(Path(business), new { email }), HttpStatusCode.Conflict);
        using var refresh = await recipient.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenResponse>())!;
        recipient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(new[] { "Employee" }, Assert.Single((await Contexts(recipient)).BusinessContexts).Roles);
        await Status(await recipient.PostAsync("/api/v1/auth/logout", null), HttpStatusCode.NoContent);
        await Status(await recipient.GetAsync("/api/v1/auth/contexts"), HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task InvitationRegistrationRequiresVerificationAndExplicitAcceptanceWithoutCustomerOrOwner()
    {
        using var root = new ApiFactory(); using var api = Api(root);
        using var owner = api.CreateClient(); using var recipient = api.CreateClient();
        var ownerEmail = Email(); var email = Email();
        await Register(api, owner, ownerEmail, true); await Login(owner, ownerEmail);
        var business = Assert.Single((await Contexts(owner)).BusinessContexts).BusinessId;
        await Invite(owner, business, email); var credential = await Mail(api, email, "employee-invitation");
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/register",
            new { token = credential, email, fullName = "Invited employee", password = Password }), HttpStatusCode.Accepted);
        await Status(await recipient.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }), HttpStatusCode.Unauthorized);
        var verification = await Mail(api, email, "email-verification");
        await Status(await recipient.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = verification }), HttpStatusCode.OK);
        await Login(recipient, email);
        var before = await Contexts(recipient); Assert.Null(before.CustomerContext); Assert.Empty(before.BusinessContexts);
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = credential }), HttpStatusCode.NoContent);
        var after = await Contexts(recipient); Assert.Null(after.CustomerContext);
        Assert.Equal(new[] { "Employee" }, Assert.Single(after.BusinessContexts).Roles);
        await using var db = Db();
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
        Assert.False(await db.CustomerProfiles.AnyAsync(x => x.UserId == user.Id));
        Assert.Equal(1, await db.BusinessMemberships.CountAsync(x => x.UserId == user.Id));
    }

    [PostgresFact]
    public async Task MultiBusinessMembershipExpiryAndLostOwnerGrantAreRecheckedByEndpoints()
    {
        using var root = new ApiFactory(); using var api = Api(root);
        using var first = api.CreateClient(); using var second = api.CreateClient(); using var recipient = api.CreateClient();
        var firstEmail = Email(); var secondEmail = Email(); var email = Email();
        await Register(api, first, firstEmail, true); await Login(first, firstEmail);
        await Register(api, second, secondEmail, true); await Login(second, secondEmail);
        await Register(api, recipient, email, false); await Login(recipient, email);
        var a = Assert.Single((await Contexts(first)).BusinessContexts).BusinessId;
        var b = Assert.Single((await Contexts(second)).BusinessContexts).BusinessId;
        await Invite(first, a, secondEmail); var credential = await Mail(api, secondEmail, "employee-invitation");
        await Status(await second.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = credential }), HttpStatusCode.NoContent);
        Assert.Equal(2, (await Contexts(second)).BusinessContexts.Count);
        var invitation = await Invite(first, a, email); credential = await Mail(api, email, "employee-invitation");
        await Status(await second.GetAsync(Path(a)), HttpStatusCode.Forbidden);
        await Status(await second.PostAsJsonAsync(Path(a), new { email = Email() }), HttpStatusCode.Forbidden);
        await Status(await second.PostAsync($"{Path(a)}/{invitation.Id}/resend", null), HttpStatusCode.Forbidden);
        await Status(await second.PostAsync($"{Path(b)}/{invitation.Id}/revoke", null), HttpStatusCode.BadRequest);
        await Status(await second.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = credential }), HttpStatusCode.BadRequest);
        await using var db = Db();
        await db.EmployeeInvitations.Where(x => x.Id == invitation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2)).SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = credential }), HttpStatusCode.BadRequest);
        invitation = await Invite(first, a, email); credential = await Mail(api, email, "employee-invitation");
        var ownerMember = (await Contexts(first)).BusinessContexts.Single().MembershipId;
        await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == ownerMember).ExecuteDeleteAsync();
        await Status(await first.GetAsync(Path(a)), HttpStatusCode.Forbidden);
        await Status(await first.PostAsync($"{Path(a)}/{invitation.Id}/revoke", null), HttpStatusCode.Forbidden);
        await Status(await first.PostAsync($"{Path(a)}/{invitation.Id}/resend", null), HttpStatusCode.Forbidden);
        await Status(await first.PostAsJsonAsync(Path(a), new { email = Email() }), HttpStatusCode.Forbidden);
        await Status(await recipient.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = credential }), HttpStatusCode.BadRequest);
        Assert.Empty((await Contexts(recipient)).BusinessContexts);
    }
}
