namespace WashFlow.Domain.Businesses;

// ISO weekday: Monday=1, Sunday=7. Local wall-clock minutes, exclusive closing boundary.
public sealed class WeeklyOpeningDay
{
    public Guid BusinessId { get; set; }
    public int Day { get; set; }
    public bool Closed { get; set; }
    public int? OpensAtMinute { get; set; }
    public int? ClosesAtMinute { get; set; }
}
