using WashFlow.Domain.Common;

namespace WashFlow.Domain.Identity;

public sealed class RefreshToken : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid DeviceSessionId { get; init; }
    public required TokenHash TokenHash { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
