using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class AppointmentResourceReservation : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid AppointmentId { get; init; }
    public Guid? WorkAreaId { get; init; }
    public Guid? EmployeeMembershipId { get; init; }
    public DateTimeOffset StartsAtUtc { get; init; }
    public DateTimeOffset EndsAtUtc { get; init; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
