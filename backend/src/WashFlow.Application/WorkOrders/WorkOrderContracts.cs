using WashFlow.Application.Scheduling;
namespace WashFlow.Application.WorkOrders;

public sealed record WorkStagePlan(string Name, int Minutes);
public sealed record CreateWorkOrderInput(Guid RequestId, uint AppointmentVersion, IReadOnlyList<WorkStagePlan>? Stages);
public sealed record WorkActionInput(Guid RequestId, uint Version);
public sealed record WorkStageView(Guid Id, int Sequence, string Name, int EstimatedMinutes, string Status,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? PausedAtUtc, long PausedMilliseconds, Guid[] EmployeeMembershipIds);
public sealed record WorkEventView(Guid RequestId, Guid? StageId, Guid ActorUserId, string Action, DateTimeOffset AtUtc);
public sealed record WorkOrderView(Guid Id, Guid BusinessId, Guid AppointmentId, uint Version, string VehiclePlate, string ServiceName,
    string Status, DateTimeOffset PlannedStartUtc, DateTimeOffset ServerNowUtc, DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<WorkStageView> Stages, IReadOnlyList<WorkEventView> Events, bool PotentialConflict, string TimeZoneId, string PlannedStartLocal, string BusinessDateLocal);
public interface IWorkOrderService
{
    Task<IReadOnlyList<WorkOrderView>> List(SchedulingActor actor, Guid business, bool owner, int page = 1, CancellationToken ct = default);
    Task<WorkOrderView> Detail(SchedulingActor actor, Guid business, Guid id, bool owner, CancellationToken ct = default);
    Task<WorkOrderView> Create(SchedulingActor actor, Guid business, Guid appointment, CreateWorkOrderInput input, CancellationToken ct = default);
    Task<WorkOrderView> Act(SchedulingActor actor, Guid business, Guid id, Guid? stage, string action, WorkActionInput input, CancellationToken ct = default);
}
