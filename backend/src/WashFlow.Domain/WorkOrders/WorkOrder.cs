using WashFlow.Domain.Common;

namespace WashFlow.Domain.WorkOrders;

public sealed record WorkStageResources(Guid WorkAreaId, Guid[] Employees);
public enum WorkStageStatus { Waiting = 1, Ready = 2, InProgress = 3, Paused = 4, Completed = 5, Cancelled = 6 }
public sealed class WorkOrder : IEntity
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public string CreationHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PlannedStartUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    private readonly List<WorkOrderStage> stages = [];
    private readonly List<WorkOrderEvent> events = [];
    public IReadOnlyList<WorkOrderStage> Stages => stages.AsReadOnly();
    public IReadOnlyList<WorkOrderEvent> Events => events.AsReadOnly();
    private WorkOrder() { }
    public static WorkOrder Create(Guid business, Guid appointment, IReadOnlyList<(string Name, int Minutes)> plans, IReadOnlyList<Guid> employees,
        Guid actor, Guid request, DateTimeOffset now, string creationHash, IReadOnlyList<WorkStageResources>? resources = null, DateTimeOffset? plannedStart = null)
    {
        if (creationHash?.Length != 64 || !creationHash.All(char.IsAsciiHexDigit) || business == Guid.Empty || appointment == Guid.Empty || actor == Guid.Empty || request == Guid.Empty ||
            plans.Count is < 1 or > 20 || plans.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 100 || x.Name.Any(char.IsControl) || x.Minutes is < 1 or > 10080) ||
            employees.Count is < 1 or > 100 || employees.Any(x => x == Guid.Empty) || employees.Distinct().Count() != employees.Count)
            throw new ArgumentException("İş emri aşamalarını ve çalışanlarını kontrol edin.");
        var order = new WorkOrder { BusinessId = business, AppointmentId = appointment, CreationHash = creationHash, PlannedStartUtc = plannedStart?.ToUniversalTime(), CreatedAt = now, UpdatedAt = now };
        if (resources != null && (resources.Count != plans.Count || resources.Any(x => x.WorkAreaId == Guid.Empty || x.Employees.Length == 0 || x.Employees.Distinct().Count() != x.Employees.Length || x.Employees.Any(e => !employees.Contains(e)))))
            throw new ArgumentException("Aşama kaynaklarını kontrol edin.");
        for (var i = 0; i < plans.Count; i++) order.stages.Add(WorkOrderStage.Create(order, i + 1, plans[i].Name.Trim(), plans[i].Minutes,
            resources == null ? employees : resources[i].Employees, now, resources?[i].WorkAreaId));
        order.events.Add(WorkOrderEvent.Create(order, null, actor, request, "create", now));
        return order;
    }
    public void Apply(Guid stageId, string action, Guid actor, Guid request, DateTimeOffset now)
    {
        if (actor == Guid.Empty || request == Guid.Empty || now < UpdatedAt || CompletedAtUtc != null || CancelledAtUtc != null)
            throw new InvalidOperationException("İş emri bu işlem için uygun değil.");
        var stage = stages.SingleOrDefault(x => x.Id == stageId) ?? throw new ArgumentException("Aşama bulunamadı.");
        stage.Apply(action, now);
        if (action == "complete")
        {
            var next = stages.SingleOrDefault(x => x.Sequence == stage.Sequence + 1);
            next?.MakeReady(now);
            if (stages.All(x => x.Status == WorkStageStatus.Completed)) CompletedAtUtc = now;
        }
        UpdatedAt = now;
        events.Add(WorkOrderEvent.Create(this, stage, actor, request, action, now));
    }
    public void Cancel(Guid actor, Guid request, DateTimeOffset now)
    {
        if (actor == Guid.Empty || request == Guid.Empty || now < UpdatedAt || CompletedAtUtc != null || CancelledAtUtc != null)
            throw new InvalidOperationException("İş emri iptal edilemez.");
        foreach (var stage in stages.Where(x => x.Status != WorkStageStatus.Completed)) stage.MarkCancelled(now);
        CancelledAtUtc = now; UpdatedAt = now;
        events.Add(WorkOrderEvent.Create(this, null, actor, request, "cancel", now));
    }
}

public sealed class WorkOrderStage : IEntity
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string Name { get; private set; } = null!;
    public int EstimatedMinutes { get; private set; }
    public Guid? WorkAreaId { get; private set; }
    public WorkStageStatus Status { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public long PausedMilliseconds { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }
    private readonly List<WorkStageAssignment> assignments = [];
    public IReadOnlyList<WorkStageAssignment> Assignments => assignments.AsReadOnly();
    private WorkOrderStage() { }
    internal static WorkOrderStage Create(WorkOrder order, int sequence, string name, int minutes, IReadOnlyList<Guid> employees, DateTimeOffset now, Guid? workArea = null)
    {
        var stage = new WorkOrderStage { BusinessId = order.BusinessId, WorkOrderId = order.Id, Sequence = sequence, Name = name, EstimatedMinutes = minutes, WorkAreaId = workArea,
            Status = sequence == 1 ? WorkStageStatus.Ready : WorkStageStatus.Waiting, CreatedAt = now, UpdatedAt = now };
        foreach (var employee in employees) stage.assignments.Add(new WorkStageAssignment(stage, employee, now));
        return stage;
    }
    internal void MakeReady(DateTimeOffset now) { Status = WorkStageStatus.Ready; UpdatedAt = now; }
    internal void MarkCancelled(DateTimeOffset now)
    {
        if (PausedAtUtc is { } paused) PausedMilliseconds = checked(PausedMilliseconds + (long)(now - paused).TotalMilliseconds);
        PausedAtUtc = null; Status = WorkStageStatus.Cancelled; UpdatedAt = now;
    }
    internal void Apply(string action, DateTimeOffset now)
    {
        switch (action, Status)
        {
            case ("start", WorkStageStatus.Ready): StartedAtUtc = now; Status = WorkStageStatus.InProgress; break;
            case ("pause", WorkStageStatus.InProgress): PausedAtUtc = now; Status = WorkStageStatus.Paused; break;
            case ("resume", WorkStageStatus.Paused):
                PausedMilliseconds = checked(PausedMilliseconds + (long)(now - PausedAtUtc!.Value).TotalMilliseconds);
                PausedAtUtc = null; Status = WorkStageStatus.InProgress; break;
            case ("complete", WorkStageStatus.InProgress): CompletedAtUtc = now; Status = WorkStageStatus.Completed; break;
            default: throw new InvalidOperationException("Bu aşama durumu için istenen işlem geçersiz.");
        }
        UpdatedAt = now;
    }
}

public sealed class WorkStageAssignment
{
    public Guid BusinessId { get; private set; }
    public Guid StageId { get; private set; }
    public Guid EmployeeMembershipId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    private WorkStageAssignment() { }
    internal WorkStageAssignment(WorkOrderStage stage, Guid employee, DateTimeOffset now)
    { BusinessId = stage.BusinessId; StageId = stage.Id; EmployeeMembershipId = employee; AssignedAtUtc = now; }
}

public sealed class WorkOrderEvent
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid? StageId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public string Action { get; private set; } = null!;
    public DateTimeOffset AtUtc { get; private set; }
    private WorkOrderEvent() { }
    internal static WorkOrderEvent Create(WorkOrder order, WorkOrderStage? stage, Guid actor, Guid request, string action, DateTimeOffset now) =>
        new() { BusinessId = order.BusinessId, WorkOrderId = order.Id, StageId = stage?.Id, ActorUserId = actor, RequestId = request, Action = action, AtUtc = now };
}
