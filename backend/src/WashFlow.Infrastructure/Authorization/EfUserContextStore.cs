using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Authorization;
using WashFlow.Domain.Businesses;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Authorization;

public sealed class EfUserContextStore(WashFlowDbContext db) : ICurrentUserContextStore
{
    public async Task<UserContexts> GetAsync(Guid userId, CancellationToken ct)
    {
        var profileId = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var grants = await (from membership in db.BusinessMemberships.AsNoTracking()
                            join business in db.Businesses.AsNoTracking() on membership.BusinessId equals business.Id
                            join grant in db.BusinessMembershipRoles.AsNoTracking() on membership.Id equals grant.BusinessMembershipId
                            where membership.UserId == userId && membership.RevokedAt == null &&
                                (grant.Role == MembershipRole.Owner || grant.Role == MembershipRole.Employee)
                            orderby business.Id, grant.Role
                            select new { business.Id, business.Name, MembershipId = membership.Id, grant.Role }).ToListAsync(ct);
        var contexts = grants.GroupBy(x => new { x.Id, x.Name, x.MembershipId })
            .Select(g => new BusinessContext(g.Key.Id, g.Key.Name, g.Key.MembershipId,
                g.Select(x => x.Role.ToString()).Distinct().ToArray())).ToArray();
        return new(profileId is { } id ? new CustomerContext(id) : null, contexts);
    }
}
