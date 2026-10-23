using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Application.Authorization;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;
using WashFlow.Infrastructure.Authorization;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class ContextAuthorizationPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    [PostgresFact]
    public async Task ContextsDiscloseOnlyOwnProfileAndActiveSupportedBusinessGrants()
    {
        using var root = new ApiFactory();
        using var api = root.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<WashFlowDbContext>();
            s.AddScoped(_ => new WashFlowDbContext(fixture.Options));
        }));
        using var client = api.CreateClient();
        await using var db = new WashFlowDbContext(fixture.Options);
        var user = User(); var other = User();
        var profile = new CustomerProfile { UserId = user.Id };
        var session = new DeviceSession { UserId = user.Id, SecurityStamp = user.SecurityStamp,
            DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        db.AddRange(user, other, profile, new CustomerProfile { UserId = other.Id }, session);
        var active = AddMembership(db, user, "Visible", MembershipRole.Owner, MembershipRole.Employee);
        var revoked = AddMembership(db, user, "Revoked", MembershipRole.Owner); revoked.RevokedAt = DateTimeOffset.UtcNow;
        AddMembership(db, user, "Grantless"); AddMembership(db, other, "Private", MembershipRole.Owner);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token);
        client.DefaultRequestHeaders.Add("X-Business-Id", revoked.BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Role", "Owner");
        using var response = await client.GetAsync("/api/v1/auth/contexts?userId=" + other.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, x => x.Name == "no-cache");
        var result = (await response.Content.ReadFromJsonAsync<UserContexts>())!;
        Assert.Equal(profile.Id, result.CustomerContext?.CustomerProfileId);
        var context = Assert.Single(result.BusinessContexts);
        Assert.Equal(active.BusinessId, context.BusinessId); Assert.Equal(active.Id, context.MembershipId);
        Assert.Equal(new[] { "Owner", "Employee" }, context.Roles);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "businessContexts", "customerContext" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(new[] { "businessId", "businessName", "membershipId", "roles" },
            json.RootElement.GetProperty("businessContexts")[0].EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(new[] { "customerProfileId" }, json.RootElement.GetProperty("customerContext").EnumerateObject().Select(x => x.Name));
        session.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        using var rejected = await client.GetAsync("/api/v1/auth/contexts");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [PostgresFact]
    public async Task CurrentGrantsAreIsolatedAndRemovalOrRevocationAppliesEvenInSameScope()
    {
        await using var db = new WashFlowDbContext(fixture.Options);
        var user = User(); var outsider = User(); db.AddRange(user, outsider);
        var a = AddMembership(db, user, "A", MembershipRole.Owner);
        var b = AddMembership(db, user, "B", MembershipRole.Employee);
        await db.SaveChangesAsync();
        var service = new BusinessAuthorizationService(new EfBusinessAuthorizationStore(db));
        Assert.True(await service.HasRoleAsync(user.Id, a.BusinessId, MembershipRole.Owner, default));
        Assert.False(await service.HasRoleAsync(user.Id, b.BusinessId, MembershipRole.Owner, default));
        Assert.False(await service.HasRoleAsync(user.Id, a.BusinessId, MembershipRole.Employee, default));
        Assert.False(await service.HasRoleAsync(outsider.Id, a.BusinessId, MembershipRole.Owner, default));
        await using (var writer = new WashFlowDbContext(fixture.Options))
        {
            await writer.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == a.Id).ExecuteDeleteAsync();
            await writer.BusinessMemberships.Where(x => x.Id == b.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }
        Assert.False(await service.HasRoleAsync(user.Id, a.BusinessId, MembershipRole.Owner, default));
        Assert.False(await service.HasRoleAsync(user.Id, b.BusinessId, MembershipRole.Employee, default));
        var contexts = await new UserContextService(new EfUserContextStore(db)).GetAsync(user.Id, default);
        Assert.Null(contexts.CustomerContext); Assert.Empty(contexts.BusinessContexts);
    }

    [PostgresFact]
    public async Task CustomerOnlyAndOwnerOnlyContextsRemainIndependent()
    {
        await using var db = new WashFlowDbContext(fixture.Options);
        var customer = User(); var owner = User(); db.AddRange(customer, owner);
        var profile = new CustomerProfile { UserId = customer.Id }; db.Add(profile);
        var membership = AddMembership(db, owner, "Owner only", MembershipRole.Owner);
        await db.SaveChangesAsync();
        var service = new UserContextService(new EfUserContextStore(db));
        var personal = await service.GetAsync(customer.Id, default);
        Assert.Equal(profile.Id, personal.CustomerContext?.CustomerProfileId);
        Assert.Empty(personal.BusinessContexts);
        var business = await service.GetAsync(owner.Id, default);
        Assert.Null(business.CustomerContext);
        Assert.Equal(membership.Id, Assert.Single(business.BusinessContexts).MembershipId);
        Assert.False(await new BusinessAuthorizationService(new EfBusinessAuthorizationStore(db))
            .HasRoleAsync(customer.Id, membership.BusinessId, MembershipRole.Owner, default));
    }

    private static User User() => new("Test", $"{Guid.NewGuid():N}@example.test", "test-only-hash")
        { EmailVerifiedAt = DateTimeOffset.UtcNow };
    private static BusinessMembership AddMembership(WashFlowDbContext db, User user, string name, params MembershipRole[] roles)
    {
        var business = new Business { Name = name };
        var membership = new BusinessMembership { UserId = user.Id, BusinessId = business.Id };
        db.AddRange(business, membership);
        foreach (var role in roles) db.Add(new BusinessMembershipRole { BusinessMembershipId = membership.Id, Role = role });
        return membership;
    }
}
