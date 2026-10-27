using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class EmployeeAvailability : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid MembershipId { get; private init; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    private EmployeeAvailability() { }
    public static EmployeeAvailability Create(Guid businessId, Guid membershipId, Occupancy window, DateTimeOffset now)
    {
        SchedulingInvariant.Id(businessId); SchedulingInvariant.Id(membershipId); window.Validate();
        return new() { BusinessId = businessId, MembershipId = membershipId, StartsAtUtc = window.Start, EndsAtUtc = window.End,
            CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
    }
    public void Update(Occupancy window, DateTimeOffset now)
    {
        window.Validate(); SchedulingInvariant.Require(ArchivedAt == null, "Arşivlenmiş uygunluk değiştirilemez.");
        now = SchedulingInvariant.ChangeTime(now, UpdatedAt);
        StartsAtUtc = window.Start; EndsAtUtc = window.End; UpdatedAt = now;
    }
    public void Archive(DateTimeOffset now)
    {
        SchedulingInvariant.Require(ArchivedAt == null, "Uygunluk zaten arşivlenmiş.");
        ArchivedAt = SchedulingInvariant.ChangeTime(now, UpdatedAt); UpdatedAt = ArchivedAt.Value;
    }
}
