using WashFlow.Application.Authorization;
using WashFlow.Domain.Businesses;

namespace WashFlow.UnitTests;

public sealed class ContextAuthorizationTests
{
    [Fact]
    public async Task DiscoveryUsesOnlySuppliedIdentityAndPreservesEmptyContexts()
    {
        var store = new Store(); var userId = Guid.NewGuid();
        var result = await new UserContextService(store).GetAsync(userId, default);
        Assert.Equal(userId, store.UserId);
        Assert.Null(result.CustomerContext); Assert.Empty(result.BusinessContexts);
        Assert.Throws<ArgumentException>(() => { _ = new UserContextService(store).GetAsync(Guid.Empty, default); });
    }

    [Fact]
    public async Task RoleChecksUseExactBusinessAndRoleAndNeverCacheDecisions()
    {
        var store = new Store(); var service = new BusinessAuthorizationService(store);
        var user = Guid.NewGuid(); var business = Guid.NewGuid();
        Assert.True(await service.HasRoleAsync(user, business, MembershipRole.Employee, default));
        Assert.Equal((user, business, MembershipRole.Employee), (store.UserId, store.BusinessId, store.Role));
        store.Allowed = false;
        Assert.False(await service.HasRoleAsync(user, business, MembershipRole.Employee, default));
        Assert.Equal(2, store.Checks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task UnsupportedRolesFailClosedWithoutQuery(int role)
    {
        var store = new Store(); var service = new BusinessAuthorizationService(store);
        Assert.False(await service.HasRoleAsync(Guid.NewGuid(), Guid.NewGuid(), (MembershipRole)role, default));
        Assert.False(await service.HasRoleAsync(Guid.Empty, Guid.NewGuid(), MembershipRole.Owner, default));
        Assert.False(await service.HasRoleAsync(Guid.NewGuid(), Guid.Empty, MembershipRole.Owner, default));
        Assert.Equal(0, store.Checks);
    }

    private sealed class Store : ICurrentUserContextStore, IBusinessAuthorizationStore
    {
        public Guid UserId, BusinessId; public MembershipRole Role; public int Checks; public bool Allowed = true;
        public Task<UserContexts> GetAsync(Guid userId, CancellationToken ct)
        { UserId = userId; return Task.FromResult(new UserContexts(null, [])); }
        public Task<bool> HasActiveGrantAsync(Guid userId, Guid businessId, MembershipRole role, CancellationToken ct)
        { UserId = userId; BusinessId = businessId; Role = role; Checks++; return Task.FromResult(Allowed); }
    }
}
