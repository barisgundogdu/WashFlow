using WashFlow.Domain.Common;

namespace WashFlow.Domain.Identity;

public sealed class EmailVerificationToken : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid UserId { get; init; }
    public required string NormalizedEmail { get; init; }
    public required Guid SecurityStamp { get; init; }
    public required TokenHash TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
