using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class AppointmentEvent : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid AppointmentId { get; private init; }
    public Guid ActorUserId { get; private init; }
    public int Sequence { get; private init; }
    public AppointmentEventKind Kind { get; private init; }
    public DateTimeOffset? PreviousStart { get; private init; }
    public DateTimeOffset? PreviousEnd { get; private init; }
    public DateTimeOffset NewStart { get; private init; }
    public DateTimeOffset NewEnd { get; private init; }
    public AppointmentStatus Status { get; private init; }
    private AppointmentEvent() { }
    internal static AppointmentEvent Create(Appointment appointment, Guid actor, AppointmentEventKind kind,
        int sequence, Occupancy? previous, Occupancy current, AppointmentStatus status, DateTimeOffset now)
    {
        SchedulingInvariant.Id(actor); current.Validate();
        SchedulingInvariant.Require(sequence > 0 && Enum.IsDefined(kind) && Enum.IsDefined(status), "Geçerli olay sırası ve durumu gereklidir.");
        return new() { BusinessId = appointment.BusinessId, AppointmentId = appointment.Id, ActorUserId = actor, Kind = kind,
            Sequence = sequence, PreviousStart = previous?.Start, PreviousEnd = previous?.End, NewStart = current.Start,
            NewEnd = current.End, Status = status, CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
    }
}
