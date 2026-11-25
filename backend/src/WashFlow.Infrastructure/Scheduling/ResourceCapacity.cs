using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.WorkOrders;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.Infrastructure.Scheduling;

/// <summary>Caller must hold the business lock. Combines planned reservations with actual running work,
/// counting each appointment once per area even where its planned and actual intervals overlap.</summary>
internal static class ResourceCapacity
{
    public static async Task<bool> Available(WashFlowDbContext db, Guid business, WorkArea area, Occupancy interval,
        Guid[] employees, Guid? excluding, DateTimeOffset now, CancellationToken ct)
    { var result = await Check(db,business,area,interval,employees,excluding,now,ct); return result.Area && result.Eligible.Length == employees.Length; }
    public static async Task<Guid[]?> Choose(WashFlowDbContext db, Guid business, WorkArea area, Occupancy interval,
        Guid[] candidates, int required, Guid? excluding, DateTimeOffset now, CancellationToken ct)
    { var result = await Check(db,business,area,interval,candidates,excluding,now,ct); return result.Area && result.Eligible.Length >= required ? result.Eligible.Take(required).ToArray() : null; }
    private static async Task<(bool Area, Guid[] Eligible)> Check(WashFlowDbContext db, Guid business, WorkArea area, Occupancy interval,
        Guid[] employees, Guid? excluding, DateTimeOffset now, CancellationToken ct)
    {
        var reservations = await db.AppointmentResourceReservations.AsNoTracking().Where(x => x.BusinessId == business && x.ReleasedAt == null && x.AppointmentId != excluding &&
            x.StartsAtUtc < interval.End && interval.Start < x.EndsAtUtc && (x.WorkAreaId == area.Id || (x.EmployeeMembershipId != null && employees.Contains(x.EmployeeMembershipId.Value)))).ToListAsync(ct);
        var stages = await db.WorkOrderStages.AsNoTracking().Where(x => x.BusinessId == business && (x.Status == WorkStageStatus.InProgress || x.Status == WorkStageStatus.Paused))
            .Include(x => x.Assignments).ToListAsync(ct);
        var orderIds = stages.Select(x => x.WorkOrderId).ToArray();
        var running = await (from order in db.WorkOrders.AsNoTracking() join appointment in db.Appointments.AsNoTracking()
            on new { order.BusinessId, Id = order.AppointmentId } equals new { appointment.BusinessId, appointment.Id }
            where order.BusinessId == business && orderIds.Contains(order.Id) && appointment.Id != excluding
            select new { OrderId = order.Id, AppointmentId = appointment.Id, appointment.WorkAreaId }).ToListAsync(ct);
        var actual = new List<(Guid Appointment, Guid Area, Guid[] Employees, Occupancy Interval)>();
        foreach (var row in running)
        {
            var stage = stages.Single(x => x.WorkOrderId == row.OrderId);
            var from = stage.StartedAtUtc!.Value;
            // Paused work still occupies its resources. An overdue stage never becomes available at estimate zero.
            var expected = from.AddMinutes(stage.EstimatedMinutes).AddMilliseconds(stage.PausedMilliseconds);
            if (stage.PausedAtUtc is { } paused) expected += now - paused;
            var until = expected > now ? expected : now.AddMinutes(1);
            var start = new DateTimeOffset(from.UtcTicks - from.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
            var endTicks = until.UtcTicks;
            if (endTicks % TimeSpan.TicksPerMinute != 0) endTicks += TimeSpan.TicksPerMinute - endTicks % TimeSpan.TicksPerMinute;
            var occupied = new Occupancy(start, new(endTicks, TimeSpan.Zero));
            if (SchedulingRules.Overlaps(occupied, interval)) actual.Add((row.AppointmentId, stage.WorkAreaId ?? row.WorkAreaId, stage.Assignments.Select(x => x.EmployeeMembershipId).ToArray(), occupied));
        }
        var areaUses = reservations.Where(x => x.WorkAreaId == area.Id).Select(x => (Appointment: x.AppointmentId, Interval: new Occupancy(x.StartsAtUtc, x.EndsAtUtc)))
            .Concat(actual.Where(x => x.Area == area.Id).Select(x => (x.Appointment, x.Interval)))
            .GroupBy(x => x.Appointment).SelectMany(g => Merge(g.Select(x => x.Interval)));
        if (!SchedulingRules.HasCapacity(areaUses, interval, area.Capacity)) return (false, []);
        var windows = await db.EmployeeAvailabilities.AsNoTracking().Where(x => x.BusinessId == business && x.ArchivedAt == null && employees.Contains(x.MembershipId) && x.StartsAtUtc < interval.End && interval.Start < x.EndsAtUtc).ToListAsync(ct);
        return (true, employees.Where(e => SchedulingRules.EmployeeAvailable(windows.Where(x => x.MembershipId == e).Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)),
            reservations.Where(x => x.EmployeeMembershipId == e).Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)).Concat(actual.Where(x => x.Employees.Contains(e)).Select(x => x.Interval)), interval)).ToArray());
    }
    private static IEnumerable<Occupancy> Merge(IEnumerable<Occupancy> source)
    {
        Occupancy? current = null;
        foreach (var item in source.OrderBy(x => x.Start))
        {
            if (current is not { } previous) current = item;
            else if (item.Start <= previous.End) current = new(previous.Start, item.End > previous.End ? item.End : previous.End);
            else { yield return previous; current = item; }
        }
        if (current is { } last) yield return last;
    }
}
