using WashFlow.Domain.Common;
using WashFlow.Domain.Identity;

namespace WashFlow.Domain.Businesses;

public sealed class EmployeeInvitation : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BusinessId { get; init; }
    public required Guid InvitedByMembershipId { get; init; }
    public required string NormalizedEmail { get; init; }
    public required TokenHash TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
