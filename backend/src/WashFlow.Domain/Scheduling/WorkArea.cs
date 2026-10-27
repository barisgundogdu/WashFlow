using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class WorkArea : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Name { get; private set; } = null!;
    public int Capacity { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    private WorkArea() { }
    public static WorkArea Create(Guid businessId, string name, int capacity, DateTimeOffset now)
    {
        SchedulingInvariant.Id(businessId);
        var area = new WorkArea { BusinessId = businessId, CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
        area.Update(name, capacity, now);
        return area;
    }
    public void Update(string name, int capacity, DateTimeOffset now)
    {
        SchedulingInvariant.Require(ArchivedAt == null && capacity > 0, "Etkin alan ve pozitif kapasite gereklidir.");
        var normalized = SchedulingInvariant.Text(name, 200);
        now = SchedulingInvariant.ChangeTime(now, CreatedAt > UpdatedAt ? CreatedAt : UpdatedAt);
        Name = normalized; Capacity = capacity; UpdatedAt = now;
    }
    public void Archive(DateTimeOffset now)
    {
        SchedulingInvariant.Require(ArchivedAt == null, "Alan zaten arşivlenmiş.");
        ArchivedAt = SchedulingInvariant.ChangeTime(now, UpdatedAt); UpdatedAt = ArchivedAt.Value;
    }
}
