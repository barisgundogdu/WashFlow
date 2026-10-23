using WashFlow.Domain.Businesses;

namespace WashFlow.Application.Authorization;

public sealed record CustomerContext(Guid CustomerProfileId);
public sealed record BusinessContext(Guid BusinessId, string BusinessName, Guid MembershipId, IReadOnlyList<string> Roles);
public sealed record UserContexts(CustomerContext? CustomerContext, IReadOnlyList<BusinessContext> BusinessContexts);

public interface ICurrentUserContextStore
{
    Task<UserContexts> GetAsync(Guid userId, CancellationToken ct);
}

public interface IBusinessAuthorizationStore
{
    Task<bool> HasActiveGrantAsync(Guid userId, Guid businessId, MembershipRole role, CancellationToken ct);
}
