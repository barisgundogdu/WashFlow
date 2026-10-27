namespace WashFlow.Application.Scheduling;

/// <summary>Owner-only application boundary. Business IDs are selectors, never authority.
/// Implementations must recheck current session and grants inside their transaction.</summary>
public interface ISchedulingService
{
    Task<WorkAreaView> SaveWorkArea(SchedulingActor actor, Guid business, Guid? id, WorkAreaInput input, CancellationToken ct = default);
    Task ArchiveWorkArea(SchedulingActor actor, Guid business, Guid id, VersionInput input, CancellationToken ct = default);
    Task<RequirementView> SaveRequirement(SchedulingActor actor, Guid business, Guid service, string category, RequirementInput input, CancellationToken ct = default);
    Task<AvailabilityView> SaveAvailability(SchedulingActor actor, Guid business, Guid employee, Guid? id, AvailabilityInput input, CancellationToken ct = default);
    Task ArchiveAvailability(SchedulingActor actor, Guid business, Guid employee, Guid id, VersionInput input, CancellationToken ct = default);
    Task<IReadOnlyList<WorkAreaView>> WorkAreas(SchedulingActor actor, Guid business, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeView>> Employees(SchedulingActor actor, Guid business, CancellationToken ct = default);
    Task<IReadOnlyList<AvailabilityView>> Availability(SchedulingActor actor, Guid business, Guid employee, CancellationToken ct = default);
    Task<IReadOnlyList<RequirementView>> Requirements(SchedulingActor actor, Guid business, CancellationToken ct = default);
    Task<SchedulingResult<AppointmentView>> Create(SchedulingActor actor, Guid business, BookingInput input, CancellationToken ct = default);
    Task<AppointmentView> Reschedule(SchedulingActor actor, Guid business, Guid id, RescheduleInput input, CancellationToken ct = default);
    Task<AppointmentView> Cancel(SchedulingActor actor, Guid business, Guid id, VersionInput input, CancellationToken ct = default);
    Task<AppointmentView> Detail(SchedulingActor actor, Guid business, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentView>> Calendar(SchedulingActor actor, Guid business, CalendarInput input, CancellationToken ct = default);
    Task<AvailabilityCheckView> Preview(SchedulingActor actor, Guid business, AvailabilityCheckInput input, CancellationToken ct = default);
}
