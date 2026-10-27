using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class ServiceSchedulingRequirement : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ServiceId { get; init; }
    public required string Category { get; init; }
    public Guid WorkAreaId { get; set; }
    public bool SingleStageConfirmed { get; set; }
}
