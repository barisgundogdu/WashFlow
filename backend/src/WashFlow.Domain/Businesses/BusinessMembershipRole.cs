namespace WashFlow.Domain.Businesses;

/// <summary>Tenant-scoped grant. A membership may have both Owner and Employee rows.</summary>
public sealed class BusinessMembershipRole
{
    public required Guid BusinessMembershipId { get; init; }
    public required MembershipRole Role { get; init; }
}
