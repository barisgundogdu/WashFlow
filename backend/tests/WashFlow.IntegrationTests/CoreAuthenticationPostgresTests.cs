using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;
using WashFlow.Infrastructure.Authentication;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class CoreAuthenticationPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private const string Password = "Test-only correct password 123!";
    private WebApplicationFactory<Program> CreateApi(ApiFactory root) => root.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<WashFlowDbContext>();
            services.AddScoped(_ => new WashFlowDbContext(fixture.Options));
        }));
    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    [PostgresFact]
    public async Task RegistrationIsUnverifiedAndOwnerGrantBelongsOnlyToCreatedBusiness()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var ownerEmail = Email(); var customerEmail = Email();
        using var ownerResponse = await client.PostAsJsonAsync("/api/v1/auth/businesses/register", new
            { ownerFullName = "Owner", businessName = "Branch", email = ownerEmail, password = Password, role = "Employee", businessId = Guid.NewGuid() });
        using var customerResponse = await client.PostAsJsonAsync("/api/v1/auth/customers/register", new
            { fullName = "Customer", email = customerEmail, password = Password, role = "Owner" });
        Assert.Equal(HttpStatusCode.Accepted, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, customerResponse.StatusCode);
        await using var db = new WashFlowDbContext(fixture.Options);
        var owner = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(ownerEmail));
        var customer = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(customerEmail));
        Assert.Null(owner.EmailVerifiedAt); Assert.Null(customer.EmailVerifiedAt);
        Assert.NotEqual(Password, owner.PasswordHash);
        var membership = await db.BusinessMemberships.SingleAsync(x => x.UserId == owner.Id);
        Assert.Equal("Branch", (await db.Businesses.SingleAsync(x => x.Id == membership.BusinessId)).Name);
        Assert.Equal(MembershipRole.Owner, (await db.BusinessMembershipRoles.SingleAsync(x => x.BusinessMembershipId == membership.Id)).Role);
        Assert.False(await db.BusinessMemberships.AnyAsync(x => x.UserId == customer.Id));
        Assert.True(await db.CustomerProfiles.AnyAsync(x => x.UserId == customer.Id));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = ownerEmail, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(await db.DeviceSessions.AnyAsync(x => x.UserId == owner.Id));
    }

    [PostgresFact]
    public async Task DuplicateAndConcurrentRegistrationsHaveIdenticalAcknowledgementsAndNoOrphanBusinesses()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var email = Email(); var businessName = "Branch-" + Guid.NewGuid();
        var body = new { ownerFullName = "Owner", businessName, email, password = Password };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/auth/businesses/register", body)));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
            await using var db = new WashFlowDbContext(fixture.Options);
            Assert.Equal(1, await db.Users.CountAsync(x => x.NormalizedEmail == User.NormalizeEmail(email)));
            Assert.Equal(1, await db.Businesses.CountAsync(x => x.Name == businessName));
            using var duplicateCustomer = await client.PostAsJsonAsync("/api/v1/auth/customers/register", new { fullName = "Customer", email, password = Password });
            Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await duplicateCustomer.Content.ReadAsStringAsync());
            var owner = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
            Assert.False(await db.CustomerProfiles.AnyAsync(x => x.UserId == owner.Id));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task UnknownWrongAndUnverifiedAccountsReturnIdenticalLoginFailures()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var verified = await Seed(api, true); var unverified = await Seed(api, false);
        var failures = new List<string>();
        foreach (var request in new[] { new { email = Email(), password = Password },
            new { email = verified.Email, password = "wrong password" }, new { email = unverified.Email, password = Password } })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            failures.Add(json.RootElement.GetProperty("code").GetString()!);
        }
        Assert.All(failures, code => Assert.Equal("invalidCredentials", code));
    }

    [PostgresFact]
    public async Task RotationHashesCredentialsPreservesAbsoluteExpiryAndReplayRevokesOnlyThatDevice()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var user = await Seed(api, true);
        var original = await Login(client, user.Email); var otherDevice = await Login(client, user.Email);
        using var rotation = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var next = (await rotation.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(original.RefreshToken, next.RefreshToken);
        Assert.Equal(original.SessionExpiresAt, next.SessionExpiresAt);
        await using (var db = new WashFlowDbContext(fixture.Options))
        {
            var hashes = await db.RefreshTokens.Join(db.DeviceSessions.Where(x => x.UserId == user.Id), t => t.DeviceSessionId, s => s.Id,
                (token, _) => token.TokenHash).ToListAsync();
            Assert.All(hashes, hash => { Assert.Equal(64, hash.Value.Length); Assert.NotEqual(next.RefreshToken, hash.Value); });
        }
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var invalidNext = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = next.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidNext.StatusCode);
        using var other = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = otherDevice.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentRefreshHasAtMostOneSuccessAndReplayRevokesItsSession()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var user = await Seed(api, true); var login = await Login(client, user.Email);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
            var winner = (await responses.Single(x => x.StatusCode == HttpStatusCode.OK)
                .Content.ReadFromJsonAsync<TokenResponse>())!;
            using var rejection = System.Text.Json.JsonDocument.Parse(await responses
                .Single(x => x.StatusCode == HttpStatusCode.Unauthorized).Content.ReadAsStringAsync());
            Assert.Equal("invalidCredentials", rejection.RootElement.GetProperty("code").GetString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", winner.AccessToken);
            using var logout = await client.PostAsync("/api/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
            using var successorRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = winner.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, successorRefresh.StatusCode);
            await using var db = new WashFlowDbContext(fixture.Options);
            var session = await db.DeviceSessions.SingleAsync(x => x.UserId == user.Id);
            Assert.NotNull(session.RevokedAt);
            var tokens = await db.RefreshTokens.Where(x => x.DeviceSessionId == session.Id).ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.All(tokens, token => Assert.True(token.RevokedAt >= token.CreatedAt));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task ReplayAndLogoutUsePostLockTimeWhenSuccessorWasCreatedAfterRequestStarted()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var user = await Seed(api, true);
        var generator = new RefreshTokenGenerator();
        // Emulate the losing request starting first but obtaining the lock after a newer rotation.
        // No sleep or probabilistic thread ordering is required to reproduce its stale timestamp.
        foreach (var logout in new[] { false, true })
        {
            var login = await Login(client, user.Email);
            var staleRequestTime = DateTimeOffset.UtcNow.AddMinutes(-1);
            var clock = new FixedClock(DateTimeOffset.UtcNow);
            await using var db = new WashFlowDbContext(fixture.Options);
            var store = new EfAuthStore(db, clock);
            var rotated = await store.RotateAsync(generator.Hash(login.RefreshToken), generator.Hash(generator.Generate()),
                clock.GetUtcNow(), default);
            Assert.NotNull(rotated);
            // Fresh context reflects the actual competing-request lifetime; no tracked state is shared.
            await using var replayDb = new WashFlowDbContext(fixture.Options);
            var replayStore = new EfAuthStore(replayDb, clock);
            if (logout) await replayStore.LogoutAsync(user.Id, rotated.Id, staleRequestTime, default);
            else Assert.Null(await replayStore.RotateAsync(generator.Hash(login.RefreshToken), generator.Hash(generator.Generate()),
                staleRequestTime, default));
            Assert.False(await replayStore.IsSessionActiveAsync(user.Id, rotated.Id, clock.GetUtcNow(), default));
            var tokens = await replayDb.RefreshTokens.AsNoTracking().Where(x => x.DeviceSessionId == rotated.Id).ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.Single(tokens, token => token.ConsumedAt != null);
            Assert.All(tokens, token => Assert.True(token.RevokedAt >= token.CreatedAt));
        }
    }

    [PostgresFact]
    public async Task RotationRejectsSessionExpiredWhileWaitingForLock()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var user = await Seed(api, true); var login = await Login(client, user.Email);
        var generator = new RefreshTokenGenerator();
        await using var db = new WashFlowDbContext(fixture.Options);
        var store = new EfAuthStore(db, new FixedClock(login.SessionExpiresAt.AddSeconds(1)));
        Assert.Null(await store.RotateAsync(generator.Hash(login.RefreshToken), generator.Hash(generator.Generate()),
            DateTimeOffset.UtcNow, default));
        var sessionId = await db.DeviceSessions.Where(x => x.UserId == user.Id).Select(x => x.Id).SingleAsync();
        var token = await db.RefreshTokens.AsNoTracking().SingleAsync(x => x.DeviceSessionId == sessionId);
        Assert.Null(token.ConsumedAt); Assert.Null(token.ReplacedByTokenId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [PostgresFact]
    public async Task LogoutRevokesCurrentJwtAndRefreshButOtherDeviceRemainsValid()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var user = await Seed(api, true);
        var current = await Login(client, user.Email); var other = await Login(client, user.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var secondLogout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, secondLogout.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = current.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var otherRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = other.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, otherRefresh.StatusCode);
    }

    private async Task<User> Seed(WebApplicationFactory<Program> api, bool verified)
    {
        var user = new User("Test", Email(), api.Services.GetRequiredService<IPasswordService>().Hash(Password));
        if (verified) user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        await using var db = new WashFlowDbContext(fixture.Options);
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }
    private static async Task<TokenResponse> Login(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, deviceName = "Test device" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }
}
