using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class ServiceSchedulingRequirement : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ServiceId { get; private init; }
    public string Category { get; private init; } = null!;
    public Guid WorkAreaId { get; private set; }
    public bool SingleStageConfirmed { get; private set; }
    private ServiceSchedulingRequirement() { }
    public static ServiceSchedulingRequirement Create(Guid businessId, Guid serviceId, string category,
        Guid workAreaId, bool singleStageConfirmed, DateTimeOffset now)
    {
        SchedulingInvariant.Id(businessId); SchedulingInvariant.Id(serviceId); SchedulingInvariant.Id(workAreaId);
        return new() { BusinessId = businessId, ServiceId = serviceId, Category = SchedulingInvariant.Text(category, 32),
            WorkAreaId = workAreaId, SingleStageConfirmed = singleStageConfirmed, CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
    }
    public void Update(Guid workAreaId, bool singleStageConfirmed, DateTimeOffset now)
    {
        SchedulingInvariant.Id(workAreaId); now = SchedulingInvariant.ChangeTime(now, UpdatedAt);
        WorkAreaId = workAreaId; SingleStageConfirmed = singleStageConfirmed; UpdatedAt = now;
    }
}
