using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Authorization;
using WashFlow.Domain.Businesses;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Authorization;

public sealed class EfBusinessAuthorizationStore(WashFlowDbContext db) : IBusinessAuthorizationStore
{
    public Task<bool> HasActiveGrantAsync(Guid userId, Guid businessId, MembershipRole role, CancellationToken ct) =>
        (from membership in db.BusinessMemberships.AsNoTracking()
         join grant in db.BusinessMembershipRoles.AsNoTracking() on membership.Id equals grant.BusinessMembershipId
         where membership.UserId == userId && membership.BusinessId == businessId && membership.RevokedAt == null &&
             grant.Role == role && (grant.Role == MembershipRole.Owner || grant.Role == MembershipRole.Employee)
         select membership.Id).AnyAsync(ct);
}
