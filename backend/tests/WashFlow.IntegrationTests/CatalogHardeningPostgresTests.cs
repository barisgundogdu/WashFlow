using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WashFlow.Application.Catalog;
using WashFlow.Application.Customers;
using WashFlow.Domain.Businesses;

namespace WashFlow.IntegrationTests;

public sealed partial class CatalogPostgresTests
{
    [PostgresFact]
    public async Task NormalizedTextLimitsRejectPostAndPutWithoutChangingExistingData()
    {
        using var root = new ApiFactory();
        using var api = Api(root);
        using var c = api.CreateClient();
        var (business, _, _) = await Seed(api, c);
        var original = await Create(c, business);
        foreach (var description in new[] { false, true })
        {
            var invalid = Input(original.Version);
            if (description) invalid.Description = new string('\u0344', 1001);
            else invalid.Name = new string('\u0344', 101);
            foreach (var update in new[] { false, true })
            {
                using var response = update
                    ? await c.PutAsJsonAsync($"{Path(business)}/{original.Id}", invalid)
                    : await c.PostAsJsonAsync(Path(business), invalid);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.True(error.RootElement.GetProperty("errors").TryGetProperty(description ? "description" : "name", out _));
            }
        }
        var current = await Json<ServiceView>(await c.GetAsync($"{Path(business)}/{original.Id}"));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(current));
        Assert.Single((await Json<DataPage<ServiceView>>(await c.GetAsync(Path(business)))).Items);

        // Valid normalization expansion at the exact storage boundary must persist canonically.
        var valid = Input();
        valid.Name = new string('\u0344', 100);
        valid.Description = new string('\u0344', 1000);
        var saved = await Create(c, business, valid);
        Assert.Equal(valid.Name.Normalize(), saved.Name);
        Assert.Equal(200, saved.Name!.Length);
        Assert.Equal(2000, saved.Description!.Length);
        var edit = Input(saved.Version);
        edit.Name = "  I\u0307c\u0327 yıkama  ";
        edit.Description = "  Ac\u0327ıklama  ";
        var after = await Json<ServiceView>(await c.PutAsJsonAsync($"{Path(business)}/{saved.Id}", edit));
        Assert.Equal("İç yıkama", after.Name);
        Assert.Equal("Açıklama", after.Description);
    }

    [PostgresFact]
    public Task PublishedReadKeepsSnapshotDuringOfferingReplacement() => AssertPublishedSnapshot("replace");
    [PostgresFact]
    public Task PublishedReadKeepsSnapshotDuringDisablement() => AssertPublishedSnapshot("disable");
    [PostgresFact]
    public Task PublishedReadKeepsSnapshotDuringArchival() => AssertPublishedSnapshot("archive");

    private async Task AssertPublishedSnapshot(string mutation)
    {
        var probe = new OfferingReadProbe();
        using var root = new ApiFactory();
        using var api = Api(root, probe);
        using var c = api.CreateClient();
        var (business, _, _) = await Seed(api, c);
        var original = await Create(c, business);
        probe.PauseNext();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var read = c.GetAsync($"/api/v1/businesses/{business}/published-services", deadline.Token);
        try
        {
            // The parent SELECT has completed; pause immediately before the child SELECT.
            await probe.Paused.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (mutation == "archive")
            {
                using var archived = await c.PostAsJsonAsync($"{Path(business)}/{original.Id}/archive", new { original.Version }, deadline.Token);
                Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
            }
            else
            {
                var edit = Input(original.Version);
                edit.Name = "Changed after snapshot";
                edit.Offerings = [new() { Category = "Suv", Price = "999.99", Currency = "TRY", ActiveMinutes = 45, Employees = 2 }];
                if (mutation == "disable") { edit.Enabled = false; edit.Published = false; }
                using var changed = await c.PutAsJsonAsync($"{Path(business)}/{original.Id}", edit, deadline.Token);
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            }
        }
        finally { probe.Release.TrySetResult(); }
        using var response = await read;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(original.Name, item.GetProperty("name").GetString());
        Assert.Equal(2, item.GetProperty("offerings").GetArrayLength());
        Assert.Equal("450.10", item.GetProperty("offerings")[0].GetProperty("price").GetString());
        Assert.False(item.GetProperty("offerings")[0].TryGetProperty("employees", out _));
        using var next = await c.GetAsync($"/api/v1/businesses/{business}/published-services");
        using var fresh = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        var items = fresh.RootElement.GetProperty("items");
        Assert.Equal(mutation == "replace" ? 1 : 0, items.GetArrayLength());
        if (mutation == "replace")
        {
            var offering = Assert.Single(items[0].GetProperty("offerings").EnumerateArray());
            Assert.Equal("Suv", offering.GetProperty("category").GetString());
            Assert.Equal("999.99", offering.GetProperty("price").GetString());
        }
    }

    [PostgresFact]
    public async Task RevokedMembershipWithOwnerGrantStillPresentCannotManageCatalog()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient();
        var (business, _, user) = await Seed(api, c);
        var row = await Create(c, business);
        await using (var db = Db())
        {
            var membership = await db.BusinessMemberships.SingleAsync(x => x.BusinessId == business && x.UserId == user);
            membership.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            Assert.True(await db.BusinessMembershipRoles.AnyAsync(x => x.BusinessMembershipId == membership.Id && x.Role == MembershipRole.Owner));
        }
        await AssertCatalogDenied(c, business, row, HttpStatusCode.Forbidden);
        using var published = await c.GetAsync($"/api/v1/businesses/{business}/published-services");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
    }

    [PostgresFact]
    public async Task EmployeeAndCustomerCannotReadUpdateOrArchivePrivateService()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var owner = api.CreateClient();
        var (business, _, _) = await Seed(api, owner);
        var row = await Create(owner, business);
        foreach (var role in new MembershipRole?[] { MembershipRole.Employee, null })
        {
            using var c = api.CreateClient();
            var (_, _, user) = await Seed(api, c, role, profile: true);
            // Exercise the exact target business rather than only a foreign nested ID.
            if (role != null)
            {
                await using var db = Db();
                var member = new BusinessMembership { BusinessId = business, UserId = user };
                db.AddRange(member, new BusinessMembershipRole { BusinessMembershipId = member.Id, Role = role.Value });
                await db.SaveChangesAsync();
            }
            await AssertCatalogDenied(c, business, row, HttpStatusCode.Forbidden);
        }
        var current = await Json<ServiceView>(await owner.GetAsync($"{Path(business)}/{row.Id}"));
        Assert.Equal(JsonSerializer.Serialize(row), JsonSerializer.Serialize(current));
    }

    [PostgresFact]
    public async Task RevokedSessionCannotAccessAnyCatalogEndpoint()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient();
        var (business, _, user) = await Seed(api, c);
        var row = await Create(c, business);
        await using (var db = Db())
        {
            var session = await db.DeviceSessions.SingleAsync(x => x.UserId == user);
            session.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await AssertCatalogDenied(c, business, row, HttpStatusCode.Unauthorized);
        using var published = await c.GetAsync($"/api/v1/businesses/{business}/published-services");
        Assert.Equal(HttpStatusCode.Unauthorized, published.StatusCode);
    }

    private static async Task AssertCatalogDenied(HttpClient c, Guid business, ServiceView row, HttpStatusCode status)
    {
        using var list = await c.GetAsync(Path(business));
        using var detail = await c.GetAsync($"{Path(business)}/{row.Id}");
        using var create = await c.PostAsJsonAsync(Path(business), Input());
        using var update = await c.PutAsJsonAsync($"{Path(business)}/{row.Id}", Input(row.Version));
        using var archive = await c.PostAsJsonAsync($"{Path(business)}/{row.Id}/archive", new { row.Version });
        Assert.All(new[] { list, detail, create, update, archive }, r => Assert.Equal(status, r.StatusCode));
    }

    [PostgresFact]
    public async Task ListsBatchOnlyReturnedServicesAndPreservePagingAndTenantIsolation()
    {
        var probe = new OfferingReadProbe();
        using var root = new ApiFactory(); using var api = Api(root, probe); using var c = api.CreateClient();
        var (business, _, _) = await Seed(api, c);
        for (var n = 0; n < 4; n++)
        {
            var input = Input(); input.Name = $"Service {n}"; input.Offerings![0].Price = $"{n}.10";
            await Create(c, business, input);
        }
        using var other = api.CreateClient(); var (foreign, _, _) = await Seed(api, other);
        await Create(other, foreign);
        foreach (var published in new[] { false, true })
        {
            probe.OfferingQueries = 0;
            var path = published ? $"/api/v1/businesses/{business}/published-services" : Path(business);
            var page = await Json<DataPage<ServiceView>>(await c.GetAsync($"{path}?pageSize=3"));
            Assert.True(page.HasMore); Assert.Equal(3, page.Items.Count); Assert.Equal(1, probe.OfferingQueries);
            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(business, page.Items[n].BusinessId);
                Assert.Equal($"Service {n}", page.Items[n].Name);
                Assert.Equal($"{n}.10", page.Items[n].Offerings![0].Price);
                Assert.Equal(2, page.Items[n].Offerings!.Count);
            }
            probe.OfferingQueries = 0;
            var second = await Json<DataPage<ServiceView>>(await c.GetAsync($"{path}?pageSize=3&page=2"));
            Assert.False(second.HasMore); Assert.Equal("Service 3", Assert.Single(second.Items).Name);
            Assert.Equal(1, probe.OfferingQueries);
            probe.OfferingQueries = 0;
            Assert.Empty((await Json<DataPage<ServiceView>>(await c.GetAsync($"{path}?q=absent"))).Items);
            Assert.Equal(0, probe.OfferingQueries);
        }
    }

    private sealed class OfferingReadProbe : DbCommandInterceptor
    {
        private int pause;
        public int OfferingQueries;
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PauseNext() => Interlocked.Exchange(ref pause, 1);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"ServiceOfferings\" AS", StringComparison.Ordinal) &&
                !command.CommandText.Contains("FROM \"BusinessServices\" AS", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref OfferingQueries);
                if (Interlocked.Exchange(ref pause, 0) == 1)
                {
                    Paused.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
