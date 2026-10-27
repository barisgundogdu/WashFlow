using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class AppointmentResourceReservation : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid AppointmentId { get; private init; }
    public Guid? WorkAreaId { get; private init; }
    public Guid? EmployeeMembershipId { get; private init; }
    public DateTimeOffset StartsAtUtc { get; private init; }
    public DateTimeOffset EndsAtUtc { get; private init; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    private AppointmentResourceReservation() { }
    public static AppointmentResourceReservation Create(Guid businessId, Guid appointmentId, Guid? workAreaId,
        Guid? employeeMembershipId, Occupancy interval, DateTimeOffset now)
    {
        SchedulingInvariant.Id(businessId); SchedulingInvariant.Id(appointmentId); interval.Validate();
        SchedulingInvariant.Require(workAreaId.HasValue != employeeMembershipId.HasValue, "Rezervasyon tam olarak bir kaynak türüne ait olmalıdır.");
        SchedulingInvariant.Id((workAreaId ?? employeeMembershipId)!.Value);
        return new() { BusinessId = businessId, AppointmentId = appointmentId, WorkAreaId = workAreaId,
            EmployeeMembershipId = employeeMembershipId, StartsAtUtc = interval.Start, EndsAtUtc = interval.End,
            CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
    }
    public void Release(DateTimeOffset now)
    {
        SchedulingInvariant.Require(ReleasedAt == null, "Rezervasyon zaten serbest bırakılmış.");
        ReleasedAt = SchedulingInvariant.ChangeTime(now, UpdatedAt); UpdatedAt = ReleasedAt.Value;
    }
}
