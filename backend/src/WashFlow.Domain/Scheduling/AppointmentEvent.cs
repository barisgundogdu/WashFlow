using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class AppointmentEvent : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid AppointmentId { get; init; }
    public Guid ActorUserId { get; init; }
    public int Sequence { get; init; }
    public required string Kind { get; init; }
    public DateTimeOffset? PreviousStart { get; init; }
    public DateTimeOffset? PreviousEnd { get; init; }
    public DateTimeOffset NewStart { get; init; }
    public DateTimeOffset NewEnd { get; init; }
    public required string Status { get; init; }
}
