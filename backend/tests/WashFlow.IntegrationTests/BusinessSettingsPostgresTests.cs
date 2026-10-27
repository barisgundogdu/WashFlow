using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Application.Businesses;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class BusinessSettingsPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root) => root.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<WashFlowDbContext>(); s.AddScoped(_ => Db()); }));
    private async Task<(Guid Business, Guid Member)> Seed(WebApplicationFactory<Program> api, HttpClient client, MembershipRole? role = MembershipRole.Owner, Guid? existingUser = null)
    {
        await using var db = Db(); var user = existingUser == null ? new User("Test", $"{Guid.NewGuid()}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow } : await db.Users.SingleAsync(x => x.Id == existingUser);
        var business = new Business { Name = "Original" }; var m = new BusinessMembership { BusinessId = business.Id, UserId = user.Id };
        if (existingUser == null) db.Add(user); db.AddRange(business, m); if (role != null) db.Add(new BusinessMembershipRole { BusinessMembershipId = m.Id, Role = role.Value });
        var session = new DeviceSession { UserId = user.Id, SecurityStamp = user.SecurityStamp, DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }; db.Add(session); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token); return (business.Id, m.Id);
    }
    private static string Path(Guid b) => $"/api/v1/businesses/{b}/settings";
    private static async Task<BusinessSettingsView> Read(HttpClient client, Guid b) { using var r = await client.GetAsync(Path(b)); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<BusinessSettingsView>())!; }
    [PostgresFact]
    public async Task ExistingBusinessStaysUnconfiguredAndProfileAndHoursPersist()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _) = await Seed(api, c);
        var r = await Read(c, b); Assert.Equal("Original", r.Name); Assert.Null(r.Phone); Assert.Null(r.TimeZoneId); Assert.Null(r.WeeklyHours);
        r.Name = "Changed"; r.Phone = "+1 (555) 010-1234"; r.AddressLine1 = "Test address"; r.City = "Test city"; r.CountryCode = "us"; r.TimeZoneId = "America/New_York"; r.WeeklyHours = Enumerable.Range(1, 7).Select(d => new OpeningDay(d, d == 7, d == 7 ? null : 0, d == 7 ? null : 1440)).ToArray();
        using var save = await c.PostAsJsonAsync(Path(b), r); Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var after = await Read(c, b); Assert.Equal("Changed", after.Name); Assert.Equal("US", after.CountryCode); Assert.Equal(r.Phone, after.Phone); Assert.Equal(r.AddressLine1, after.AddressLine1); Assert.Equal(r.City, after.City); Assert.Equal("America/New_York", after.TimeZoneId); Assert.Equal(7, after.WeeklyHours!.Count); Assert.True(after.WeeklyHours.Single(x => x.Day == 7).Closed); Assert.NotEqual(r.Version, after.Version);
        after.WeeklyHours = null; using var clear = await c.PostAsJsonAsync(Path(b), after); Assert.Equal(HttpStatusCode.OK, clear.StatusCode); Assert.Null((await Read(c, b)).WeeklyHours);
    }
    [PostgresFact]
    public async Task AnonymousEmployeeCustomerAndForeignOwnerCannotAccessPrivateSettings()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var owner = api.CreateClient(); var (b, _) = await Seed(api, owner);
        using var anonymous = api.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(b))).StatusCode);
        foreach (var role in new MembershipRole?[] { MembershipRole.Employee, null, MembershipRole.Owner })
        {
            using var other = api.CreateClient(); await Seed(api, other, role); var data = await Read(owner, b);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(Path(b))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(Path(b), data)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(Path(Guid.NewGuid()))).StatusCode);
    }
    [PostgresFact]
    public async Task ConcurrentEditorsGetOneWinnerAndNoLostUpdate()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _) = await Seed(api, c); var one = await Read(c, b); var two = await Read(c, b); one.Name = "First"; two.Name = "Second";
        var responses = await Task.WhenAll(c.PostAsJsonAsync(Path(b), one), c.PostAsJsonAsync(Path(b), two)); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = (await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<BusinessSettingsView>())!; Assert.Equal(winner.Name, (await Read(c, b)).Name);
        foreach (var r in responses) r.Dispose();
    }
    [PostgresFact]
    public async Task SameOwnerMultipleBusinessesAreIndependentAndRevocationIsRechecked()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (a, member) = await Seed(api, c);
        await using var db = Db(); var user = await db.BusinessMemberships.Where(x => x.Id == member).Select(x => x.UserId).SingleAsync(); var (b, _) = await Seed(api, c, existingUser: user);
        var input = await Read(c, a); input.Name = "Only A"; using var result = await c.PostAsJsonAsync(Path(a), input); Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal("Original", (await Read(c, b)).Name);
        await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == member).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Path(a))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Path(a), input)).StatusCode); Assert.Equal("Original", (await Read(c, b)).Name);
    }
    [PostgresFact]
    public async Task InvalidAndMalformedSchedulesFailWithoutPartialWrites()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _) = await Seed(api, c); var r = await Read(c, b); r.Name = "Must not persist"; r.WeeklyHours = [new(1, false, 1200, 600)];
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Path(b), r)).StatusCode);
        using var malformed = new StringContent("{\"weeklyHours\":[null]}", System.Text.Encoding.UTF8, "application/json"); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync(Path(b), malformed)).StatusCode); Assert.Equal("Original", (await Read(c, b)).Name);
    }

    [PostgresFact]
    public async Task UpgradeKeepsLegacyBusinessNameAndDoesNotInventContactOrHours()
    {
        await using var template = Db();
        var schema = "washflow_upgrade_" + Guid.NewGuid().ToString("N");
        var original = template.Database.GetConnectionString()!;
        await using var connection = new NpgsqlConnection(original); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(original) { SearchPath = schema, Pooling = false };
            var options = new DbContextOptionsBuilder<WashFlowDbContext>().UseNpgsql(builder.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using var db = new WashFlowDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261023112302_EmployeeInvitationLifecycle");
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Businesses\" (\"Id\",\"Name\",\"CreatedAt\",\"UpdatedAt\") VALUES ({id},{"Legacy business"},{now},{now})");
            await db.Database.MigrateAsync();
            var b = await db.Businesses.SingleAsync(x => x.Id == id); Assert.Equal("Legacy business", b.Name); Assert.Null(b.Phone); Assert.Null(b.AddressLine1); Assert.Null(b.TimeZoneId); Assert.False(b.HoursConfigured); Assert.Empty(await db.WeeklyOpeningDays.ToListAsync());
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }
}
