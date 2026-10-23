using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Invitations;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Invitations;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.IntegrationTests;
[Trait("Category", "PostgreSQL")]
public sealed class InvitationPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private readonly InvitationTokens tokens = new();
    private WashFlowDbContext Db() => new(fixture.Options);
    private EfInvitationStore Store(WashFlowDbContext db, TimeProvider? clock = null) => new(db, clock ?? TimeProvider.System);
    private async Task<(User Owner, User Recipient, Business Business)> Seed()
    {
        await using var db = Db();
        var owner = new User("Owner", $"{Guid.NewGuid()}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var recipient = new User("Employee", $"{Guid.NewGuid()}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var business = new Business { Name = "Test" };
        var member = new BusinessMembership { BusinessId = business.Id, UserId = owner.Id };
        db.AddRange(owner, recipient, business, member, new BusinessMembershipRole { BusinessMembershipId = member.Id, Role = MembershipRole.Owner });
        await db.SaveChangesAsync(); return (owner, recipient, business);
    }
    private async Task<InvitationResult> Issue(User owner, User recipient, Business business, string raw, Guid? resend = null)
    {
        await using var db = Db(); return await Store(db).IssueAsync(owner.Id, business.Id, recipient.NormalizedEmail, resend, tokens.Hash(raw), TimeSpan.FromHours(24), default);
    }
    [PostgresFact]
    public async Task ConcurrentCreationAndAcceptanceAreSingleUseAndEmployeeOnly()
    {
        var (owner, recipient, business) = await Seed(); var raw = tokens.Generate();
        var issued = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Issue(owner, recipient, business, raw)));
        Assert.Single(issued, x => x.Status == InvitationStatus.Success);
        var accepted = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = Db(); return await Store(db).AcceptAsync(recipient.Id, tokens.Hash(raw), default); }));
        Assert.Single(accepted, x => x == InvitationStatus.Success);
        await using var check = Db(); var membership = await check.BusinessMemberships.SingleAsync(x => x.UserId == recipient.Id && x.BusinessId == business.Id);
        Assert.Equal(MembershipRole.Employee, (await check.BusinessMembershipRoles.SingleAsync(x => x.BusinessMembershipId == membership.Id)).Role);
        Assert.Equal(InvitationStatus.Conflict, (await Issue(owner, recipient, business, tokens.Generate())).Status);
    }
    [PostgresFact]
    public async Task ReplacementRevocationEmailMismatchAndTenantAuthorizationAreEnforced()
    {
        var (owner, recipient, business) = await Seed(); var raw = tokens.Generate();
        var issued = await Issue(owner, recipient, business, raw); var next = tokens.Generate();
        var replacement = await Issue(owner, recipient, business, next, issued.Value!.Id);
        await using var db = Db();
        Assert.Equal(InvitationStatus.Invalid, await Store(db).AcceptAsync(recipient.Id, tokens.Hash(raw), default));
        Assert.Equal(InvitationStatus.Invalid, await Store(db).AcceptAsync(owner.Id, tokens.Hash(next), default));
        Assert.Equal(InvitationStatus.Forbidden, await Store(db).RevokeAsync(recipient.Id, business.Id, replacement.Value!.Id, default));
        Assert.Equal(InvitationStatus.Invalid, await Store(db).RevokeAsync(owner.Id, business.Id, Guid.NewGuid(), default));
        Assert.Equal(InvitationStatus.Success, await Store(db).RevokeAsync(owner.Id, business.Id, replacement.Value.Id, default));
        Assert.Equal(InvitationStatus.Invalid, await Store(db).AcceptAsync(recipient.Id, tokens.Hash(next), default));
    }
    [PostgresFact]
    public async Task LostInviterGrantExpiryAndHistoricalOwnerMembershipFailClosed()
    {
        var (owner, recipient, business) = await Seed(); var raw = tokens.Generate(); await Issue(owner, recipient, business, raw);
        await using var db = Db();
        Assert.Equal(InvitationStatus.Invalid, await Store(db, new FutureClock()).AcceptAsync(recipient.Id, tokens.Hash(raw), default));
        var old = new BusinessMembership { BusinessId = business.Id, UserId = recipient.Id, RevokedAt = DateTimeOffset.UtcNow };
        db.AddRange(old, new BusinessMembershipRole { BusinessMembershipId = old.Id, Role = MembershipRole.Owner }); await db.SaveChangesAsync();
        Assert.Equal(InvitationStatus.Conflict, await Store(db).AcceptAsync(recipient.Id, tokens.Hash(raw), default));
        Assert.NotNull((await db.BusinessMemberships.SingleAsync(x => x.Id == old.Id)).RevokedAt);
        await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId != old.Id).ExecuteDeleteAsync();
        Assert.Equal(InvitationStatus.Invalid, await Store(db).AcceptAsync(recipient.Id, tokens.Hash(raw), default));
    }
    [PostgresFact]
    public async Task RegistrationCreatesIdentityAndVerificationOnlyWithoutConsumingInvitation()
    {
        var (owner, _, business) = await Seed();
        var user = new User("New employee", $"{Guid.NewGuid()}@example.test", "hash");
        var raw = tokens.Generate();
        await Issue(owner, user, business, raw);
        var verification = new EmailVerificationToken { UserId = user.Id, NormalizedEmail = user.NormalizedEmail,
            SecurityStamp = user.SecurityStamp, TokenHash = tokens.Hash(tokens.Generate()), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        await using var db = Db();
        Assert.True(await Store(db).RegisterAsync(user, verification, tokens.Hash(raw), default));
        Assert.False(await db.CustomerProfiles.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await db.BusinessMemberships.AnyAsync(x => x.UserId == user.Id));
        Assert.Equal(InvitationStatus.Invalid, await Store(db).AcceptAsync(user.Id, tokens.Hash(raw), default));
        user.EmailVerifiedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        Assert.Equal(InvitationStatus.Success, await Store(db).AcceptAsync(user.Id, tokens.Hash(raw), default));
        Assert.Equal(1, await db.Businesses.CountAsync(x => x.Id == business.Id));
    }
    [PostgresFact]
    public async Task AcceptanceRacesWithResendAndRevocationWithoutInconsistentState()
    {
        foreach (var resend in new[] { false, true })
        {
            var (owner, recipient, business) = await Seed(); var raw = tokens.Generate();
            var issued = await Issue(owner, recipient, business, raw);
            var next = tokens.Generate();
            var accept = Task.Run(async () => { await using var db = Db(); return await Store(db).AcceptAsync(recipient.Id, tokens.Hash(raw), default); });
            var change = Task.Run(async () =>
            {
                await using var db = Db();
                return resend ? (await Store(db).IssueAsync(owner.Id, business.Id, null, issued.Value!.Id, tokens.Hash(next), TimeSpan.FromHours(24), default)).Status
                    : await Store(db).RevokeAsync(owner.Id, business.Id, issued.Value!.Id, default);
            });
            await Task.WhenAll(accept, change);
            Assert.True(accept.Result == InvitationStatus.Success || change.Result == InvitationStatus.Success);
            Assert.False(accept.Result == InvitationStatus.Success && change.Result == InvitationStatus.Success);
            await using var check = Db();
            var row = await check.EmployeeInvitations.SingleAsync(x => x.Id == issued.Value!.Id);
            Assert.False(row.AcceptedAt is not null && row.RevokedAt is not null);
            Assert.Equal(accept.Result == InvitationStatus.Success, await check.BusinessMemberships.AnyAsync(x => x.UserId == recipient.Id && x.BusinessId == business.Id));
        }
    }
    [PostgresFact]
    public async Task ForeignBusinessOwnerCannotReadOrMutateInvitations()
    {
        var (owner, recipient, business) = await Seed(); var (otherOwner, _, otherBusiness) = await Seed();
        var raw = tokens.Generate(); var issued = await Issue(owner, recipient, business, raw);
        await using var db = Db();
        Assert.Null(await Store(db).ListAsync(otherOwner.Id, business.Id, default));
        Assert.Empty((await Store(db).ListAsync(otherOwner.Id, otherBusiness.Id, default))!);
        Assert.Equal(InvitationStatus.Invalid, await Store(db).RevokeAsync(otherOwner.Id, otherBusiness.Id, issued.Value!.Id, default));
        Assert.Equal(InvitationStatus.Forbidden, await Store(db).RevokeAsync(otherOwner.Id, business.Id, issued.Value.Id, default));
        Assert.Equal(InvitationStatus.Conflict, (await Issue(owner, owner, business, tokens.Generate())).Status);
    }
    [PostgresFact]
    public async Task HttpNewEmployeeVerifiesLogsInAndExplicitlyAccepts()
    {
        var (owner, _, business) = await Seed();
        using var root = new ApiFactory();
        using var api = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<WashFlowDbContext>();
            services.AddScoped(_ => new WashFlowDbContext(fixture.Options));
        }));
        using var client = api.CreateClient();
        var session = new DeviceSession { UserId = owner.Id, SecurityStamp = owner.SecurityStamp, DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        await using (var seed = Db()) { seed.Add(session); await seed.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token);
        var email = $"{Guid.NewGuid()}@example.test";
        var path = $"/api/v1/businesses/{business.Id}/employee-invitations";
        var created = await client.PostAsJsonAsync(path, new { email });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = (await created.Content.ReadFromJsonAsync<InvitationView>())!;
        var listed = await client.GetStringAsync(path);
        Assert.DoesNotContain("tokenHash", listed); Assert.Contains(view.Id.ToString(), listed);
        var mailbox = api.Services.GetRequiredService<DevelopmentEmailSender>();
        async Task<VerificationEmail> Receive(string purpose)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                if (mailbox.TryTake(email, purpose, out var message)) return message!;
                await Task.Delay(10, timeout.Token);
            }
        }
        var invite = await Receive("employee-invitation");
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/employee-invitations/register",
            new { email, fullName = "New employee", password = "test-password", token = invite.Token })).StatusCode);
        var verification = await Receive("email-verification");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = verification.Token })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "test-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var credentials = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        await using var check = Db(); var user = await check.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
        Assert.False(await check.CustomerProfiles.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await check.BusinessMemberships.AnyAsync(x => x.UserId == user.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = invite.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/employee-invitations/accept", new { token = invite.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        var contexts = await client.GetStringAsync("/api/v1/auth/contexts");
        Assert.Contains("Employee", contexts); Assert.DoesNotContain("Owner", contexts);
    }
    [PostgresFact]
    public async Task ExpiryIsRecheckedAfterWaitingForRecipientLock()
    {
        var (owner, recipient, business) = await Seed(); var raw = tokens.Generate();
        await Issue(owner, recipient, business, raw);
        await using var blocker = Db(); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {recipient.Id} FOR UPDATE").ToListAsync();
        var clock = new ControlledClock();
        var attempt = Task.Run(async () => { await using var db = Db(); return await Store(db, clock).AcceptAsync(recipient.Id, tokens.Hash(raw), default); });
        // Both pending checks finish before waiting on the recipient; advance expiry before releasing its lock.
        await clock.Resolved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance();
        await tx.CommitAsync();
        Assert.Equal(InvitationStatus.Invalid, await attempt.WaitAsync(TimeSpan.FromSeconds(10)));
        await using var check = Db();
        Assert.False(await check.BusinessMemberships.AnyAsync(x => x.UserId == recipient.Id && x.BusinessId == business.Id));
        Assert.Null((await check.EmployeeInvitations.SingleAsync(x => x.TokenHash == tokens.Hash(raw))).AcceptedAt);
    }
    private sealed class ControlledClock : TimeProvider
    {
        private int calls, advanced;
        public TaskCompletionSource Resolved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Advance() => Interlocked.Exchange(ref advanced, 1);
        public override DateTimeOffset GetUtcNow()
        {
            var now = DateTimeOffset.UtcNow.AddDays(Volatile.Read(ref advanced) == 0 ? 0 : 2);
            if (Interlocked.Increment(ref calls) >= 4) Resolved.TrySetResult();
            return now;
        }
    }
    private sealed class FutureClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(2); }
}
