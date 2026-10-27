using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class WorkArea : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string Name { get; set; }
    public int Capacity { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
