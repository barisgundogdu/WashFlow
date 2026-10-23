using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;
using WashFlow.Infrastructure.Email;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class EmailVerificationPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private const string Password = "Test-only correct password!";
    private WebApplicationFactory<Program> CreateApi(ApiFactory root) => root.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<WashFlowDbContext>();
            services.AddScoped(_ => new WashFlowDbContext(fixture.Options));
        }));

    [PostgresFact]
    public async Task RegistrationIssuesOnlyHashAndVerificationEnablesLoginWithoutChangingTenantRoles()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        using var registered = await client.PostAsJsonAsync("/api/v1/auth/businesses/register", new
            { ownerFullName = "Owner", businessName = "Branch", email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        var message = await Receive(api, email);
        await using var db = new WashFlowDbContext(fixture.Options);
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
        var token = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(new VerificationTokenGenerator().Hash(message.Token), token.TokenHash);
        Assert.Null(user.EmailVerifiedAt);
        var grants = await db.BusinessMembershipRoles.Join(db.BusinessMemberships.Where(x => x.UserId == user.Id),
            role => role.BusinessMembershipId, membership => membership.Id, (role, _) => role.Role).ToListAsync();
        using var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        using var verified = await client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = message.Token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = message.Token });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(grants, await db.BusinessMembershipRoles.Join(db.BusinessMemberships.Where(x => x.UserId == user.Id),
            role => role.BusinessMembershipId, membership => membership.Id, (role, _) => role.Role).ToListAsync());
        Assert.False(await db.CustomerProfiles.AnyAsync(x => x.UserId == user.Id));
    }

    [PostgresFact]
    public async Task CustomerRegistrationTokenAndUserAreSavedTogether()
    {
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsJsonAsync("/api/v1/auth/customers/register", new { fullName = "Test", email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await Receive(api, email);
        await using var db = new WashFlowDbContext(fixture.Options);
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
        Assert.True(await db.CustomerProfiles.AnyAsync(x => x.UserId == user.Id));
        Assert.True(await db.EmailVerificationTokens.AnyAsync(x => x.UserId == user.Id && x.TokenHash == new VerificationTokenGenerator().Hash(message.Token)));
        Assert.False(await db.BusinessMemberships.AnyAsync(x => x.UserId == user.Id));
    }

    [PostgresFact]
    public async Task ConcurrentVerificationHasExactlyOneWinner()
    {
        var (user, raw) = await Seed();
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = raw })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
            await using var db = new WashFlowDbContext(fixture.Options);
            Assert.NotNull((await db.Users.SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt);
            Assert.NotNull((await db.EmailVerificationTokens.SingleAsync(x => x.UserId == user.Id)).ConsumedAt);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task ExpiredRevokedWrongEmailAndWrongSecurityStampTokensAreRejected()
    {
        foreach (var mode in new[] { "expired", "revoked", "email", "stamp" })
        {
            var (user, raw) = await Seed(mode);
            await using var db = new WashFlowDbContext(fixture.Options);
            Assert.False(await new EfEmailVerificationStore(db, TimeProvider.System).VerifyAsync(new VerificationTokenGenerator().Hash(raw), DateTimeOffset.UtcNow, default));
            Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt);
        }
    }

    [PostgresFact]
    public async Task ResendRevokesOldTokenAndConcurrentResendsObserveSharedCooldown()
    {
        var (user, raw) = await Seed();
        // Initial issue is older than the default cooldown.
        var options = new VerificationOptions();
        var generator = new VerificationTokenGenerator();
        var first = generator.Generate(); var second = generator.Generate();
        await using var firstDb = new WashFlowDbContext(fixture.Options);
        await using var secondDb = new WashFlowDbContext(fixture.Options);
        var recipients = await Task.WhenAll(
            new EfEmailVerificationStore(firstDb, TimeProvider.System).ReplaceAsync(user.NormalizedEmail, generator.Hash(first), DateTimeOffset.UtcNow, options, default),
            new EfEmailVerificationStore(secondDb, TimeProvider.System).ReplaceAsync(user.NormalizedEmail, generator.Hash(second), DateTimeOffset.UtcNow, options, default));
        Assert.Single(recipients, x => x is not null);
        await using var db = new WashFlowDbContext(fixture.Options);
        var store = new EfEmailVerificationStore(db, TimeProvider.System);
        Assert.False(await store.VerifyAsync(generator.Hash(raw), DateTimeOffset.UtcNow, default));
        var winner = recipients[0] is not null ? first : second;
        Assert.True(await store.VerifyAsync(generator.Hash(winner), DateTimeOffset.UtcNow, default));
    }

    [PostgresFact]
    public async Task DuplicateRegistrationDoesNotGenerateExtraPendingTokensOrMemberships()
    {
        var (user, _) = await Seed();
        using var root = new ApiFactory(); using var api = CreateApi(root); using var client = api.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/businesses/register", new
            { ownerFullName = "Other", businessName = "Unwanted", email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var db = new WashFlowDbContext(fixture.Options);
        Assert.Equal(1, await db.EmailVerificationTokens.CountAsync(x => x.UserId == user.Id));
        Assert.False(await db.BusinessMemberships.AnyAsync(x => x.UserId == user.Id));
    }

    [PostgresFact]
    public async Task VerificationUsesFreshClockAfterUserLockRatherThanRequestStartTime()
    {
        var (user, raw) = await Seed();
        await using var db = new WashFlowDbContext(fixture.Options);
        var store = new EfEmailVerificationStore(db, new FutureClock());
        Assert.False(await store.VerifyAsync(new VerificationTokenGenerator().Hash(raw), DateTimeOffset.UtcNow, default));
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt);
    }

    private sealed class FutureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(2);
    }

    [PostgresFact]
    public async Task UpgradeMigrationRetainsOnlyNewestLegacyPendingToken()
    {
        var schema = "washflow_upgrade_" + Guid.NewGuid().ToString("N");
        await using var template = new WashFlowDbContext(fixture.Options);
        var connectionString = template.Database.GetConnectionString()!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
            var options = new DbContextOptionsBuilder<WashFlowDbContext>()
                .UseNpgsql(settings.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using var db = new WashFlowDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261021123220_InitialIdentityAndTenancy");
            var user = new User("Test", $"{Guid.NewGuid():N}@example.test", "encoded-test-hash");
            var now = DateTimeOffset.UtcNow;
            EmailVerificationToken Token(int age) => new() { UserId = user.Id, NormalizedEmail = user.NormalizedEmail,
                SecurityStamp = user.SecurityStamp, TokenHash = new VerificationTokenGenerator().Hash(new VerificationTokenGenerator().Generate()),
                CreatedAt = now.AddMinutes(-age), ExpiresAt = now.AddHours(1) };
            var old = Token(10); var latest = Token(1);
            db.AddRange(user, old, latest);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync();
            var active = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == user.Id && x.ConsumedAt == null && x.RevokedAt == null);
            Assert.Equal(latest.Id, active.Id);
            Assert.NotNull((await db.EmailVerificationTokens.SingleAsync(x => x.Id == old.Id)).RevokedAt);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task<(User User, string Token)> Seed(string? mode = null)
    {
        var user = new User("Test", $"{Guid.NewGuid():N}@example.test", "encoded-test-hash");
        var raw = new VerificationTokenGenerator().Generate();
        var now = DateTimeOffset.UtcNow;
        var token = new EmailVerificationToken { UserId = user.Id, NormalizedEmail = mode == "email" ? "OTHER@EXAMPLE.TEST" : user.NormalizedEmail,
            SecurityStamp = mode == "stamp" ? Guid.NewGuid() : user.SecurityStamp,
            TokenHash = new VerificationTokenGenerator().Hash(raw), CreatedAt = now.AddMinutes(-5),
            ExpiresAt = mode == "expired" ? now.AddMinutes(-1) : now.AddMinutes(55), RevokedAt = mode == "revoked" ? now : null };
        await using var db = new WashFlowDbContext(fixture.Options);
        db.AddRange(user, token); await db.SaveChangesAsync(); return (user, raw);
    }

    private static async Task<VerificationEmail> Receive(WebApplicationFactory<Program> api, string email)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var mailbox = api.Services.GetRequiredService<DevelopmentEmailSender>();
        while (!timeout.IsCancellationRequested)
        {
            if (mailbox.TryTake(email, out var message)) return message!;
            await Task.Delay(10, timeout.Token);
        }
        throw new InvalidOperationException("Test e-postası teslim edilmedi.");
    }
}
