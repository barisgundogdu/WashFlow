using WashFlow.Domain.Businesses;

namespace WashFlow.Application.Authorization;

/// <summary>Checks current grants only. Callers must also scope resources and validate assignments.
/// For writes, use this check inside the mutation transaction with appropriate locking/rechecks.</summary>
public sealed class BusinessAuthorizationService(IBusinessAuthorizationStore store)
{
    public Task<bool> HasRoleAsync(Guid userId, Guid businessId, MembershipRole requiredRole, CancellationToken ct)
    {
        if (userId == Guid.Empty || businessId == Guid.Empty ||
            requiredRole is not (MembershipRole.Owner or MembershipRole.Employee)) return Task.FromResult(false);
        return store.HasActiveGrantAsync(userId, businessId, requiredRole, ct);
    }
}
