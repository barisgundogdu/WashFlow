using WashFlow.Domain.Common;

namespace WashFlow.Domain.Businesses;

public sealed class BusinessMembership : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BusinessId { get; init; }
    public required Guid UserId { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
