using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class ServiceStageTemplate : IEntity
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string Category { get; private set; } = null!;
    public int Sequence { get; private set; }
    public string Name { get; private set; } = null!;
    public int Minutes { get; private set; }
    public Guid WorkAreaId { get; private set; }
    public int Employees { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }
    private ServiceStageTemplate() { }
    public static ServiceStageTemplate Create(Guid business, Guid service, string category, int sequence, StageResourceSnapshot plan, DateTimeOffset now)
    {
        SchedulingInvariant.Id(business); SchedulingInvariant.Id(service); plan.Validate();
        SchedulingInvariant.Require(sequence is >= 1 and <= 20, "Aşama sırası geçersiz.");
        return new() { BusinessId = business, ServiceId = service, Category = SchedulingInvariant.Text(category,32), Sequence = sequence,
            Name = SchedulingInvariant.Text(plan.Name,100), Minutes = plan.Minutes, WorkAreaId = plan.WorkAreaId, Employees = plan.Employees, CreatedAt = now, UpdatedAt = now };
    }
    public void Archive(DateTimeOffset now) { ArchivedAt = SchedulingInvariant.ChangeTime(now, UpdatedAt); UpdatedAt = now; }
}
public sealed record StageResourceSnapshot(string Name, int Minutes, Guid WorkAreaId, int Employees)
{
    public void Validate()
    {
        SchedulingInvariant.Text(Name,100); SchedulingInvariant.Id(WorkAreaId);
        SchedulingInvariant.Require(Minutes is >= 1 and <= 10080 && Employees is >= 1 and <= 100, "Aşama süresini ve çalışan sayısını kontrol edin.");
    }
}
public sealed class AppointmentStage : IEntity
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public int Sequence { get; private set; }
    public string Name { get; private set; } = null!;
    public int OffsetMinutes { get; private set; }
    public int Minutes { get; private set; }
    public Guid WorkAreaId { get; private set; }
    public int Employees { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }
    private AppointmentStage() { }
    internal static AppointmentStage Create(Appointment appointment, int sequence, int offset, StageResourceSnapshot plan, DateTimeOffset now) => new()
    { BusinessId = appointment.BusinessId, AppointmentId = appointment.Id, Sequence = sequence, Name = plan.Name.Trim(), OffsetMinutes = offset,
        Minutes = plan.Minutes, WorkAreaId = plan.WorkAreaId, Employees = plan.Employees, CreatedAt = now, UpdatedAt = now };
}
