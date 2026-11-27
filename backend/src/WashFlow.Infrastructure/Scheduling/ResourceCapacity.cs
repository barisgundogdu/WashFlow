using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.WorkOrders;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.Infrastructure.Scheduling;

/// <summary>Authoritative writers hold the business lock. Read-only slot suggestions may reuse one
/// snapshot; booking always reloads it under that lock. No employee data leaves this boundary.</summary>
internal static class ResourceCapacity
{
    internal sealed record ActualUse(Guid Appointment,Guid Area,Guid[] Employees,Occupancy Interval);
    internal sealed class Snapshot(IReadOnlyList<AppointmentResourceReservation> reservations,IReadOnlyList<EmployeeAvailability> windows,IReadOnlyList<ActualUse> actual)
    {
        public Guid[]? Choose(WorkArea area,Occupancy interval,Guid[] candidates,int required,Guid? excluding)
        {
            var busy=reservations.Where(x=>x.AppointmentId!=excluding&&x.StartsAtUtc<interval.End&&interval.Start<x.EndsAtUtc).ToArray();
            var running=actual.Where(x=>x.Appointment!=excluding&&SchedulingRules.Overlaps(x.Interval,interval)).ToArray();
            var areaUses=busy.Where(x=>x.WorkAreaId==area.Id).Select(x=>(Appointment:x.AppointmentId,Interval:new Occupancy(x.StartsAtUtc,x.EndsAtUtc)))
                .Concat(running.Where(x=>x.Area==area.Id).Select(x=>(x.Appointment,x.Interval))).GroupBy(x=>x.Appointment).SelectMany(g=>Merge(g.Select(x=>x.Interval)));
            if(!SchedulingRules.HasCapacity(areaUses,interval,area.Capacity))return null;
            var eligible=candidates.Where(e=>SchedulingRules.EmployeeAvailable(windows.Where(w=>w.MembershipId==e).Select(w=>new Occupancy(w.StartsAtUtc,w.EndsAtUtc)),
                busy.Where(x=>x.EmployeeMembershipId==e).Select(x=>new Occupancy(x.StartsAtUtc,x.EndsAtUtc)).Concat(running.Where(x=>x.Employees.Contains(e)).Select(x=>x.Interval)),interval)).Take(required).ToArray();
            return eligible.Length==required?eligible:null;
        }
        public bool Available(WorkArea area,Occupancy interval,Guid[] employees,Guid? excluding)=>Choose(area,interval,employees,employees.Length,excluding)!=null;
    }
    public static async Task<Snapshot> Load(WashFlowDbContext db,Guid business,DateTimeOffset from,DateTimeOffset to,Guid[] employees,DateTimeOffset now,CancellationToken ct)
    {
        var reservations=await db.AppointmentResourceReservations.AsNoTracking().Where(x=>x.BusinessId==business&&x.ReleasedAt==null&&x.StartsAtUtc<to&&from<x.EndsAtUtc).ToListAsync(ct);
        var stages=await db.WorkOrderStages.AsNoTracking().Where(x=>x.BusinessId==business&&(x.Status==WorkStageStatus.InProgress||x.Status==WorkStageStatus.Paused)).Include(x=>x.Assignments).ToListAsync(ct);
        var orderIds=stages.Select(x=>x.WorkOrderId).ToArray();
        var running=await(from order in db.WorkOrders.AsNoTracking() join appointment in db.Appointments.AsNoTracking()
            on new{order.BusinessId,Id=order.AppointmentId} equals new{appointment.BusinessId,appointment.Id}
            where order.BusinessId==business&&orderIds.Contains(order.Id)
            select new{OrderId=order.Id,AppointmentId=appointment.Id,appointment.WorkAreaId}).ToListAsync(ct);
        var actual=new List<ActualUse>();
        foreach(var row in running)
        {
            var stage=stages.Single(x=>x.WorkOrderId==row.OrderId);var start=stage.StartedAtUtc!.Value;
            var expected=start.AddMinutes(stage.EstimatedMinutes).AddMilliseconds(stage.PausedMilliseconds);
            if(stage.PausedAtUtc is{}paused)expected+=now-paused;
            var until=expected>now?expected:now.AddMinutes(1);
            var startMinute=new DateTimeOffset(start.UtcTicks-start.UtcTicks%TimeSpan.TicksPerMinute,TimeSpan.Zero);
            var ticks=until.UtcTicks;if(ticks%TimeSpan.TicksPerMinute!=0)ticks+=TimeSpan.TicksPerMinute-ticks%TimeSpan.TicksPerMinute;
            var occupied=new Occupancy(startMinute,new(ticks,TimeSpan.Zero));
            if(occupied.Start<to&&from<occupied.End)actual.Add(new(row.AppointmentId,stage.WorkAreaId??row.WorkAreaId,stage.Assignments.Select(x=>x.EmployeeMembershipId).ToArray(),occupied));
        }
        var windows=await db.EmployeeAvailabilities.AsNoTracking().Where(x=>x.BusinessId==business&&x.ArchivedAt==null&&employees.Contains(x.MembershipId)&&x.StartsAtUtc<to&&from<x.EndsAtUtc).ToListAsync(ct);
        return new(reservations,windows,actual);
    }
    public static async Task<bool> Available(WashFlowDbContext db,Guid business,WorkArea area,Occupancy interval,Guid[] employees,Guid? excluding,DateTimeOffset now,CancellationToken ct)=>
        (await Load(db,business,interval.Start,interval.End,employees,now,ct)).Available(area,interval,employees,excluding);
    public static async Task<Guid[]?> Choose(WashFlowDbContext db,Guid business,WorkArea area,Occupancy interval,Guid[] candidates,int required,Guid? excluding,DateTimeOffset now,CancellationToken ct)=>
        (await Load(db,business,interval.Start,interval.End,candidates,now,ct)).Choose(area,interval,candidates,required,excluding);
    private static IEnumerable<Occupancy> Merge(IEnumerable<Occupancy> source)
    {
        Occupancy? current=null;
        foreach(var item in source.OrderBy(x=>x.Start))
        {
            if(current is not{}previous)current=item;
            else if(item.Start<=previous.End)current=new(previous.Start,item.End>previous.End?item.End:previous.End);
            else{yield return previous;current=item;}
        }
        if(current is{}last)yield return last;
    }
}
