using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WashFlow.Application.Authentication;
using WashFlow.Application.Customers;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class CustomerVehiclePostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root) => root.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<WashFlowDbContext>(); s.AddScoped(_ => Db()); }));
    private async Task<(Guid Business, Guid Profile, Guid User)> Seed(WebApplicationFactory<Program> api, HttpClient c, MembershipRole? role = MembershipRole.Owner, bool profile = false, Guid? sameUser = null)
    {
        await using var db = Db(); var u = sameUser == null ? new User("Test", $"{Guid.NewGuid()}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow } : await db.Users.SingleAsync(u => u.Id == sameUser);
        var b = new Business { Name = "Test business" }; var m = new BusinessMembership { BusinessId = b.Id, UserId = u.Id }; if (sameUser == null) db.Add(u); db.AddRange(b, m); if (role != null) db.Add(new BusinessMembershipRole { BusinessMembershipId = m.Id, Role = role.Value });
        var p = new CustomerProfile { UserId = u.Id }; if (profile) db.Add(p);
        var session = new DeviceSession { UserId = u.Id, SecurityStamp = u.SecurityStamp, DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }; db.Add(session); await db.SaveChangesAsync();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token); return (b.Id, p.Id, u.Id);
    }
    private static string Customers(Guid b) => $"/api/v1/businesses/{b}/customers";
    private static string Vehicles(Guid b, Guid c) => $"{Customers(b)}/{c}/vehicles";
    private static VehicleInput Vehicle(string plate = "34 abc123") => new() { Plate = plate, Brand = "Test brand", Model = "Test model", Category = "Car", ModelYear = 2020 };
    private static async Task<T> Json<T>(HttpResponseMessage r, HttpStatusCode code = HttpStatusCode.OK) { using (r) { Assert.Equal(code, r.StatusCode); return (await r.Content.ReadFromJsonAsync<T>())!; } }
    private static async Task<CustomerView> Create(HttpClient c, Guid b, string name = "Ayşe Yılmaz", string phone = "+90 (555) 123-45-67") => await Json<CustomerView>(await c.PostAsJsonAsync(Customers(b), new CustomerInput { FullName = name, Phone = phone }), HttpStatusCode.Created);
    [PostgresFact]
    public async Task OwnerCrudVehicleNormalizationSearchAndPaginationPersist()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _, _) = await Seed(api, c);
        var customer = await Create(c, b); var read = await Json<CustomerView>(await c.GetAsync($"{Customers(b)}/{customer.Id}")); Assert.Equal(customer.Id, read.Id);
        var edit = new CustomerInput { FullName = "Edited person", Phone = customer.Phone, Email = "local@example.test", Version = customer.Version }; var updated = await Json<CustomerView>(await c.PutAsJsonAsync($"{Customers(b)}/{customer.Id}", edit)); Assert.NotEqual(customer.Version, updated.Version);
        var v = await Json<VehicleView>(await c.PostAsJsonAsync(Vehicles(b, customer.Id), Vehicle()), HttpStatusCode.Created); Assert.Equal("34 ABC 123", v.Plate); Assert.Equal(customer.Id, v.LocalCustomerId);
        var change = Vehicle("D-MÜ 123"); change.Version = v.Version; change.Color = "Blue"; var after = await Json<VehicleView>(await c.PutAsJsonAsync($"{Vehicles(b, customer.Id)}/{v.Id}", change)); Assert.Equal("D-MÜ 123", after.Plate); Assert.Equal("Blue", after.Color);
        foreach (var q in new[] { "edited", "555123", "d-mü123" }) { var page = await Json<DataPage<CustomerView>>(await c.GetAsync($"{Customers(b)}?q={Uri.EscapeDataString(q)}")); Assert.Equal(customer.Id, Assert.Single(page.Items).Id); }
        var global = await Json<DataPage<VehicleView>>(await c.GetAsync($"/api/v1/businesses/{b}/vehicles?q=edited")); Assert.Equal(v.Id, Assert.Single(global.Items).Id);
        await Create(c, b, "İlker"); Assert.Single((await Json<DataPage<CustomerView>>(await c.GetAsync($"{Customers(b)}?q=ilker"))).Items); var first = await Json<DataPage<CustomerView>>(await c.GetAsync($"{Customers(b)}?pageSize=1")); Assert.True(first.HasMore); var second = await Json<DataPage<CustomerView>>(await c.GetAsync($"{Customers(b)}?pageSize=1&page=2")); Assert.False(second.HasMore); Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"{Customers(b)}?pageSize=5000")).StatusCode);
        await using var db = Db(); Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == "LOCAL@EXAMPLE.TEST"));
    }
    [PostgresFact]
    public async Task TenantAndNestedIdsAreUntrustedAcrossAllOperations()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var a = api.CreateClient(); using var other = api.CreateClient(); var (b, _, _) = await Seed(api, a); var (foreign, _, _) = await Seed(api, other);
        var c = await Create(a, b); var c2 = await Create(a, b, "Another"); var v = await Json<VehicleView>(await a.PostAsJsonAsync(Vehicles(b, c.Id), Vehicle()), HttpStatusCode.Created);
        foreach (var client in new[] { other })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Customers(b))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Customers(b), new CustomerInput { FullName = "Forged" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Customers(b)}/{c.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Customers(b)}/{c.Id}", new CustomerInput { FullName = "Forged", Version = c.Version })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Vehicles(b, c.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Vehicles(b, c.Id), Vehicle())).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Customers(foreign)}/{c.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Vehicles(foreign, c.Id))).StatusCode);
        var update = Vehicle(); update.Version = v.Version;
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync($"{Vehicles(b, c2.Id)}/{v.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"{Vehicles(b, c2.Id)}/{v.Id}/archive", new { version = v.Version })).StatusCode);
        Assert.Empty((await Json<DataPage<CustomerView>>(await other.GetAsync($"{Customers(foreign)}?q=555123"))).Items);
    }
    [PostgresFact]
    public async Task AnonymousEmployeeAndCustomerCannotManageLocalRecords()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var owner = api.CreateClient(); var (b, _, _) = await Seed(api, owner); using var anon = api.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Customers(b))).StatusCode);
        foreach (var role in new MembershipRole?[] { MembershipRole.Employee, null }) { using var c = api.CreateClient(); await Seed(api, c, role, true); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Customers(b))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Customers(b), new CustomerInput { FullName = "Not allowed" })).StatusCode); }
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/me/vehicles")).StatusCode);
    }
    [PostgresFact]
    public async Task AccountVehiclesStayOwnedAndDuplicatesDoNotLinkAccountsOrLocalRecords()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var one = api.CreateClient(); using var two = api.CreateClient(); var (b, p, _) = await Seed(api, one, profile: true); var (_, otherProfile, otherUser) = await Seed(api, two, null, true);
        var local = await Create(one, b); var localVehicle = await Json<VehicleView>(await one.PostAsJsonAsync(Vehicles(b, local.Id), Vehicle()), HttpStatusCode.Created);
        var v = await Json<VehicleView>(await one.PostAsJsonAsync("/api/v1/me/vehicles", Vehicle()), HttpStatusCode.Created); var duplicate = await Json<VehicleView>(await one.PostAsJsonAsync("/api/v1/me/vehicles", Vehicle()), HttpStatusCode.Created); Assert.NotEqual(v.Id, duplicate.Id); Assert.Null(v.BusinessId); Assert.Null(v.LocalCustomerId);
        Assert.Empty((await Json<DataPage<VehicleView>>(await two.GetAsync("/api/v1/me/vehicles"))).Items);
        var update = Vehicle("ab-123-cd"); update.Version = v.Version;
        Assert.Equal(HttpStatusCode.NotFound, (await two.PutAsJsonAsync($"/api/v1/me/vehicles/{v.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await two.PostAsJsonAsync($"/api/v1/me/vehicles/{v.Id}/archive", new { version = v.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await one.PutAsJsonAsync($"{Vehicles(b, local.Id)}/{v.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await one.PutAsJsonAsync($"/api/v1/me/vehicles/{localVehicle.Id}", update)).StatusCode);
        var forged = await Json<VehicleView>(await one.PostAsJsonAsync("/api/v1/me/vehicles", new { plate = "35 ABC 123", brand = "Test", model = "Test", category = "Car", customerProfileId = otherProfile, userId = otherUser }), HttpStatusCode.Created);
        await using (var ownership = Db()) Assert.Equal(p, (await ownership.OwnedVehicles.SingleAsync(x => x.Id == forged.Id)).CustomerProfileId);
        var changed = await Json<VehicleView>(await one.PutAsJsonAsync($"/api/v1/me/vehicles/{v.Id}", update)); Assert.Equal("ab-123-cd", changed.Plate);
        Assert.Equal(HttpStatusCode.NoContent, (await one.PostAsJsonAsync($"/api/v1/me/vehicles/{v.Id}/archive", new { version = changed.Version })).StatusCode);
        Assert.Equal(2, (await Json<DataPage<VehicleView>>(await one.GetAsync("/api/v1/me/vehicles"))).Items.Count);
        await using var db = Db(); Assert.NotNull((await db.OwnedVehicles.SingleAsync(x => x.Id == v.Id)).ArchivedAt); Assert.Equal(p, (await db.OwnedVehicles.SingleAsync(x => x.Id == duplicate.Id)).CustomerProfileId); Assert.Equal(1, await db.LocalCustomers.CountAsync(x => x.BusinessId == b));
    }
    [PostgresFact]
    public async Task VersionsPreventSilentOverwriteAndArchivedRowsCannotBeEdited()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _, _) = await Seed(api, c); var customer = await Create(c, b);
        var requests = await Task.WhenAll(Enumerable.Range(1, 2).Select(i => c.PutAsJsonAsync($"{Customers(b)}/{customer.Id}", new CustomerInput { FullName = $"Edit {i}", Version = customer.Version }))); Assert.Single(requests, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Conflict); foreach (var r in requests) r.Dispose();
        var v = await Json<VehicleView>(await c.PostAsJsonAsync(Vehicles(b, customer.Id), Vehicle()), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync($"{Vehicles(b, customer.Id)}/{v.Id}/archive", new { version = v.Version })).StatusCode); var update = Vehicle(); update.Version = v.Version; Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync($"{Vehicles(b, customer.Id)}/{v.Id}", update)).StatusCode);
    }
    [PostgresFact]
    public async Task RequiredOwnershipForeignKeysAndRestrictDeletesAreEnforced()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _, _) = await Seed(api, c); var customer = await Create(c, b); await using var db = Db();
        LocalVehicle V(Guid business, Guid parent) => new() { BusinessId = business, LocalCustomerId = parent, Plate = "34 ABC 123", NormalizedPlate = "34ABC123", Brand = "Test", Model = "Test", Category = "Car" };
        foreach (var row in new[] { V(Guid.NewGuid(), customer.Id), V(b, Guid.NewGuid()) }) { db.Add(row); var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(e.InnerException).SqlState); db.ChangeTracker.Clear(); }
        db.Add(V(b, customer.Id)); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.Remove(await db.LocalCustomers.SingleAsync(x => x.Id == customer.Id)); var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }
    [PostgresFact]
    public async Task MultiBusinessOwnerScopesAndGrantRevocationAreRechecked()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (a, _, u) = await Seed(api, c); var (b, _, _) = await Seed(api, c, sameUser: u); var customer = await Create(c, a); Assert.Empty((await Json<DataPage<CustomerView>>(await c.GetAsync(Customers(b)))).Items); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Customers(b)}/{customer.Id}")).StatusCode);
        await using var db = Db(); var m = await db.BusinessMemberships.SingleAsync(x => x.BusinessId == a && x.UserId == u); await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == m.Id).ExecuteDeleteAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Customers(a))).StatusCode); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Customers(b))).StatusCode);
    }
    [PostgresFact]
    public async Task InvalidRequestsAreRejectedWithoutPartialRecords()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var (b, _, _) = await Seed(api, c); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Customers(b), new CustomerInput { FullName = "" })).StatusCode); var customer = await Create(c, b); var input = Vehicle(); input.ModelYear = 3000; Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Vehicles(b, customer.Id), input)).StatusCode); Assert.Empty((await Json<DataPage<VehicleView>>(await c.GetAsync(Vehicles(b, customer.Id)))).Items);
    }
}
