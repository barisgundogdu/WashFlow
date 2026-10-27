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
using WashFlow.Application.Catalog;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed partial class CatalogPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root, Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor? interceptor = null) => root.WithWebHostBuilder(b => b.ConfigureServices(s => {
        s.RemoveAll<WashFlowDbContext>();
        s.AddScoped(_ => interceptor == null ? Db() : new WashFlowDbContext(
            new DbContextOptionsBuilder<WashFlowDbContext>(fixture.Options).AddInterceptors(interceptor).Options));
    }));
    private async Task<(Guid Business, Guid Profile, Guid User)> Seed(WebApplicationFactory<Program> api, HttpClient c, MembershipRole? role = MembershipRole.Owner, bool profile = false, Guid? sameUser = null)
    {
        await using var db = Db(); var u = sameUser == null ? new User("Test", $"{Guid.NewGuid()}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow } : await db.Users.SingleAsync(u => u.Id == sameUser);
        var b = new Business { Name = "Test business" }; var m = new BusinessMembership { BusinessId = b.Id, UserId = u.Id }; if (sameUser == null) db.Add(u); db.AddRange(b, m); if (role != null) db.Add(new BusinessMembershipRole { BusinessMembershipId = m.Id, Role = role.Value });
        var p = new CustomerProfile { UserId = u.Id }; if (profile) db.Add(p);
        var session = new DeviceSession { UserId = u.Id, SecurityStamp = u.SecurityStamp, DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }; db.Add(session); await db.SaveChangesAsync();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token); return (b.Id, p.Id, u.Id);
    }
    private static string Path(Guid b) => $"/api/v1/businesses/{b}/services";
    private static ServiceInput Input(uint? version=null) => new() { Name="Dış yıkama", Description="Test", Enabled=true, Published=true, Version=version, Offerings=[new() {Category="Car",Price="450.10",Currency="TRY",ActiveMinutes=20,WaitingMinutes=0,Employees=1},new() {Category="Motorcycle",Price="0.00",Currency="TRY",ActiveMinutes=10,Employees=1}] };
    private static async Task<T> Json<T>(HttpResponseMessage r, HttpStatusCode code=HttpStatusCode.OK) { using(r) {Assert.Equal(code,r.StatusCode);return (await r.Content.ReadFromJsonAsync<T>())!;} }
    private static async Task<ServiceView> Create(HttpClient c,Guid b, ServiceInput? i=null) => await Json<ServiceView>(await c.PostAsJsonAsync(Path(b),i??Input()),HttpStatusCode.Created);
    [PostgresFact]
    public async Task OwnerCrudOfferingsSearchFiltersAndArchivePreserveHistory()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var (b,_,_)=await Seed(api,c);
        var row=await Create(c,b);Assert.Equal("450.10",row.Offerings![0].Price);Assert.Equal(2,row.Offerings.Count);
        var read=await Json<ServiceView>(await c.GetAsync($"{Path(b)}/{row.Id}"));Assert.Equal(row.Version,read.Version);
        var edit=Input(row.Version);edit.Name="İç temizlik";edit.Published=false;edit.Offerings=[edit.Offerings![0]];
        var after=await Json<ServiceView>(await c.PutAsJsonAsync($"{Path(b)}/{row.Id}",edit));Assert.NotEqual(row.Version,after.Version);Assert.Single(after.Offerings!);
        Assert.Single((await Json<DataPage<ServiceView>>(await c.GetAsync($"{Path(b)}?q=iç"))).Items);
        Assert.Empty((await Json<DataPage<ServiceView>>(await c.GetAsync($"{Path(b)}?status=disabled"))).Items);
        await Create(c,b);var first=await Json<DataPage<ServiceView>>(await c.GetAsync($"{Path(b)}?pageSize=1"));Assert.True(first.HasMore);Assert.False((await Json<DataPage<ServiceView>>(await c.GetAsync($"{Path(b)}?pageSize=1&page=2"))).HasMore);
        Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync($"{Path(b)}/{row.Id}/archive",new {version=after.Version})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"{Path(b)}/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.PutAsJsonAsync($"{Path(b)}/{row.Id}",Input(after.Version))).StatusCode);
        await using var db=Db();var archived=await db.BusinessServices.SingleAsync(x=>x.Id==row.Id);Assert.False(archived.Available);Assert.NotNull(archived.ArchivedAt);Assert.Single(await db.ServiceOfferings.Where(x=>x.ServiceId==row.Id).ToListAsync());
    }
    [PostgresFact]
    public async Task PublishedCatalogIsTenantScopedAndHasOnlyAvailableSafeFields()
    {
        using var root=new ApiFactory();using var api=Api(root);using var a=api.CreateClient();using var other=api.CreateClient();var(b,_,_)=await Seed(api,a);var(foreign,_,_)=await Seed(api,other);
        var visible=await Create(a,b);await Create(other,foreign);
        var hidden=Input();hidden.Published=false;await Create(a,b,hidden);hidden.Enabled=false;await Create(a,b,hidden);
        var archived=await Create(a,b);await a.PostAsJsonAsync($"{Path(b)}/{archived.Id}/archive",new {version=archived.Version});
        await using(var unavailable=Db()) { unavailable.Add(new BusinessService{BusinessId=b,Name="Unavailable empty service",SearchName="UNAVAILABLE",Enabled=true,Published=true});await unavailable.SaveChangesAsync(); }
        using var response=await other.GetAsync($"/api/v1/businesses/{b}/published-services");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("no-store",response.Headers.CacheControl!.ToString());
        using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());var items=json.RootElement.GetProperty("items");Assert.Equal(1,items.GetArrayLength());var item=items[0];Assert.Equal(visible.Id,item.GetProperty("id").GetGuid());Assert.Equal(b,item.GetProperty("businessId").GetGuid());Assert.False(item.TryGetProperty("version",out _));Assert.False(item.GetProperty("offerings")[0].TryGetProperty("employees",out _));
        Assert.Empty((await Json<DataPage<ServiceView>>(await other.GetAsync($"/api/v1/businesses/{Guid.NewGuid()}/published-services"))).Items);
        using var anonymous=api.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/v1/businesses/{b}/published-services")).StatusCode);
    }
    [PostgresFact]
    public async Task TenantNestedIdAndRoleBoundariesAreEnforced()
    {
        using var root=new ApiFactory();using var api=Api(root);using var a=api.CreateClient();using var other=api.CreateClient();var(b,_,_)=await Seed(api,a);var(foreign,_,_)=await Seed(api,other);var row=await Create(a,b);
        Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync(Path(b))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await other.PostAsJsonAsync(Path(b),Input())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"{Path(foreign)}/{row.Id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.PutAsJsonAsync($"{Path(foreign)}/{row.Id}",Input(row.Version))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsJsonAsync($"{Path(foreign)}/{row.Id}/archive",new {version=row.Version})).StatusCode);
        foreach(var role in new MembershipRole?[]{MembershipRole.Employee,null}) {using var c=api.CreateClient();var (own,_,_)=await Seed(api,c,role,true);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(own))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync(Path(own),Input())).StatusCode);}
    }
    [PostgresFact]
    public async Task ConcurrentEditsAndArchiveRaceNeverSilentlyOverwrite()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var(b,_,_)=await Seed(api,c);var row=await Create(c,b);
        var r=await Task.WhenAll(c.PutAsJsonAsync($"{Path(b)}/{row.Id}",Input(row.Version)),c.PutAsJsonAsync($"{Path(b)}/{row.Id}",Input(row.Version)));
        Assert.Single(r,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(r,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var x in r)x.Dispose();
        var current=await Json<ServiceView>(await c.GetAsync($"{Path(b)}/{row.Id}"));
        r=await Task.WhenAll(c.PutAsJsonAsync($"{Path(b)}/{row.Id}",Input(current.Version)),c.PostAsJsonAsync($"{Path(b)}/{row.Id}/archive",new {version=current.Version}));
        Assert.Single(r,x=>x.IsSuccessStatusCode);Assert.Single(r,x=>x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);foreach(var x in r)x.Dispose();
    }
    [PostgresFact]
    public async Task RevocationIsRecheckedAndInvalidWritesLeaveOfferingsUntouched()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var(b,_,u)=await Seed(api,c);var row=await Create(c,b);
        using(var anonymous=api.CreateClient()) {Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(Path(b))).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync(Path(b),Input())).StatusCode);}
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"{Path(b)}/{row.Id}/archive",new {version=0})).StatusCode);
        var invalid=Input(row.Version);invalid.Offerings![0].Price="1.001";Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync($"{Path(b)}/{row.Id}",invalid)).StatusCode);
        var after=await Json<ServiceView>(await c.GetAsync($"{Path(b)}/{row.Id}"));Assert.Equal(row.Version,after.Version);Assert.Equal(2,after.Offerings!.Count);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync($"{Path(b)}?status=forged")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync($"{Path(b)}?pageSize=1000")).StatusCode);
        await using var db=Db();var m=await db.BusinessMemberships.SingleAsync(x=>x.BusinessId==b&&x.UserId==u);await db.BusinessMembershipRoles.Where(x=>x.BusinessMembershipId==m.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(b))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.PutAsJsonAsync($"{Path(b)}/{row.Id}",Input(row.Version))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync($"{Path(b)}/{row.Id}/archive",new {version=row.Version})).StatusCode);
    }
    [PostgresFact]
    public async Task DatabaseConstraintsAndRestrictedHistoryAreEnforced()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var(b,_,_)=await Seed(api,c);var row=await Create(c,b);await using var db=Db();
        foreach(var category in new[]{"Car","Unknown"}) { db.Add(new ServiceOffering{ServiceId=row.Id,Category=category,Price=1,Currency="TRY",ActiveMinutes=1,Employees=1});var e=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.IsType<PostgresException>(e.InnerException);db.ChangeTracker.Clear(); }
        db.Remove(await db.BusinessServices.SingleAsync(x=>x.Id==row.Id));var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }
    [PostgresFact]
    public async Task FailedOfferingReplacementRollsBackParentAndChildrenAtomically()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var(b,_,u)=await Seed(api,c);var row=await Create(c,b);
        await using(var db=Db()) {
            var bad=Input(row.Version);bad.Name="Should roll back";bad.Offerings![0].Price="-1";bad.Offerings=[bad.Offerings[0]];
            var store=new WashFlow.Infrastructure.Catalog.EfCatalogStore(db);
            await Assert.ThrowsAsync<DbUpdateException>(()=>store.Save(u,b,row.Id,bad,CancellationToken.None));
        }
        var current=await Json<ServiceView>(await c.GetAsync($"{Path(b)}/{row.Id}"));Assert.Equal(row.Name,current.Name);Assert.Equal(row.Version,current.Version);Assert.Equal(2,current.Offerings!.Count);
    }
    [PostgresFact]
    public async Task DatabasePriceCurrencyEstimateAndForeignKeyChecksProtectWrites()
    {
        using var root=new ApiFactory();using var api=Api(root);using var c=api.CreateClient();var(b,_,_)=await Seed(api,c);var row=await Create(c,b);await using var db=Db();
        for(var n=0;n<6;n++) {
            var o=new ServiceOffering{ServiceId=row.Id,Category="Other",Price=1,Currency="TRY",ActiveMinutes=1,Employees=1};
            switch(n){case 0:o.Price=-1;break;case 1:o.Currency="USD";break;case 2:o.ActiveMinutes=0;break;case 3:o.WaitingMinutes=43201;break;case 4:o.Employees=101;break;case 5:o=new ServiceOffering{ServiceId=Guid.NewGuid(),Category="Car",Price=1,Currency="TRY",ActiveMinutes=1,Employees=1};break;}
            db.Add(o);var e=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.IsType<PostgresException>(e.InnerException);db.ChangeTracker.Clear();
        }
    }
}
