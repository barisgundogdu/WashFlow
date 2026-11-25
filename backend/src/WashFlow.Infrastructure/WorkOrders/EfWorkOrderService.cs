using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Businesses;
using WashFlow.Application.Scheduling;
using WashFlow.Application.WorkOrders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.WorkOrders;
using WashFlow.Infrastructure.Persistence;
using static WashFlow.Application.Scheduling.SchedulingRules;
namespace WashFlow.Infrastructure.WorkOrders;

public sealed class EfWorkOrderService(WashFlowDbContext db, TimeProvider clock) : IWorkOrderService
{
    private IQueryable<WorkOrder> Orders(Guid business) => db.WorkOrders.Where(x => x.BusinessId == business)
        .Include(x => x.Stages.OrderBy(s => s.Sequence)).ThenInclude(x => x.Assignments).Include(x => x.Events.OrderBy(e => e.AtUtc).ThenBy(e => e.Id)).AsSplitQuery();
    private async Task<T> Run<T>(SchedulingActor actor, Guid business, bool owner, Func<Business, Guid, Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Bağımsız işlem gereklidir.");
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var b = await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {business} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (b == null) Fail("forbidden", "İşletme erişimi gerekli.", 403);
            var m = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {business} AND \"UserId\" = {actor.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (m == null || m.RevokedAt != null) Fail("forbidden", "Etkin üyelik gerekli.", 403);
            var role = owner ? 1 : 2;
            if ((await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {m.Id} AND \"Role\" = {role} FOR UPDATE").AsNoTracking().ToListAsync(ct)).Count != 1)
                Fail("forbidden", "Bu işlem için yetkiniz yok.", 403);
            var user = await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var session = await db.DeviceSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {actor.SessionId} AND \"UserId\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var now = clock.GetUtcNow();
            if (user == null || session == null || session.RevokedAt != null || session.ExpiresAt <= now || session.CreatedAt > now || session.SecurityStamp != user.SecurityStamp)
                Fail("unauthorized", "Oturum geçersiz.", 401);
            var result = await action(b, m.Id); await tx.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException) { throw new SchedulingFault(409, "scheduleConflict", "Kayıt değişti; yeniden yükleyin."); }
        finally { db.ChangeTracker.Clear(); }
    }
    private uint Version<T>(T row) where T : class => db.Entry(row).Property<uint>("Version").CurrentValue;
    private async Task<WorkOrderView> View(WorkOrder order, bool owner, Guid member, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.BusinessId == order.BusinessId && x.Id == order.AppointmentId, ct);
        var now = clock.GetUtcNow();
        var currentZone = db.Businesses.Local.Single(x => x.Id == order.BusinessId).TimeZoneId;
        var displayZone = Zone(string.IsNullOrWhiteSpace(currentZone) ? a.TimeZoneIdSnapshot : currentZone);
        var stages = order.Stages.Where(x => owner || x.Assignments.Any(y => y.EmployeeMembershipId == member)).Select(x => new WorkStageView(x.Id, x.Sequence, x.Name, x.EstimatedMinutes, x.Status.ToString(),
            x.StartedAtUtc, x.CompletedAtUtc, x.PausedAtUtc, x.PausedMilliseconds, x.Assignments.Select(y => y.EmployeeMembershipId).ToArray())).ToArray();
        var overdue = stages.Any(x => x.StartedAtUtc != null && x.Status is "InProgress" or "Paused" &&
            ((x.PausedAtUtc ?? now) - x.StartedAtUtc.Value).TotalMilliseconds - x.PausedMilliseconds > x.EstimatedMinutes * 60000L);
        var status = order.CancelledAtUtc != null ? "Cancelled" : order.CompletedAtUtc != null ? "Completed" :
            order.Stages.Any(x => x.Status == WorkStageStatus.Paused) ? "Paused" : order.Stages.Any(x => x.Status == WorkStageStatus.InProgress) ? "InProgress" : "Scheduled";
        return new(order.Id, order.BusinessId, order.AppointmentId, Version(order), a.VehiclePlate, a.ServiceName, status, a.StartsAtUtc, now, order.CompletedAtUtc,
            stages, owner ? order.Events.Select(x => new WorkEventView(x.RequestId, x.StageId, x.ActorUserId, x.Action, x.AtUtc)).ToArray() : [], overdue, displayZone.Id, TimeZoneInfo.ConvertTime(a.StartsAtUtc, displayZone).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneInfo.ConvertTime(now, displayZone).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }
    private async Task<WorkOrder> Load(Guid business, Guid id, bool owner, Guid member, CancellationToken ct)
    {
        var q = Orders(business).Where(x => x.Id == id);
        if (!owner) q = q.Where(x => x.Stages.Any(s => s.Assignments.Any(a => a.EmployeeMembershipId == member)));
        return await q.SingleOrDefaultAsync(ct) ?? throw new SchedulingFault(404, "resourceNotFound", "İş emri bulunamadı.");
    }
    public Task<IReadOnlyList<WorkOrderView>> List(SchedulingActor actor, Guid business, bool owner, int page = 1, CancellationToken ct = default) => Run<IReadOnlyList<WorkOrderView>>(actor, business, owner, async (_, member) =>
    {
        if (page is < 1 or > 10000) Fail("validation", "Geçerli sayfa seçin.", 400);
        var q = Orders(business);
        if (!owner) q = q.Where(x => x.Stages.Any(s => s.Assignments.Any(a => a.EmployeeMembershipId == member)));
        var rows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 30).Take(30).ToListAsync(ct);
        var views = new List<WorkOrderView>(); foreach (var row in rows) views.Add(await View(row, owner, member, ct)); return views;
    }, ct);
    public Task<WorkOrderView> Detail(SchedulingActor actor, Guid business, Guid id, bool owner, CancellationToken ct = default) =>
        Run(actor, business, owner, async (_, member) => await View(await Load(business, id, owner, member, ct), owner, member, ct), ct);
    public Task<WorkOrderView> Create(SchedulingActor actor, Guid business, Guid appointment, CreateWorkOrderInput input, CancellationToken ct = default) => Run(actor, business, true, async (_, member) =>
    {
        if (input.RequestId == Guid.Empty || input.AppointmentVersion == 0 || input.Stages is not { Count: >= 1 and <= 20 } ||
            input.Stages.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 100 || x.Name.Any(char.IsControl) || x.Minutes is < 1 or > 10080))
            Fail("validation", "Aşamaları ve sürelerini kontrol edin.", 400);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { appointment, input.AppointmentVersion, Stages = input.Stages.Select(x => new { Name = x.Name.Trim(), x.Minutes }) }))));
        var previous = await db.WorkOrderEvents.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == business && x.RequestId == input.RequestId, ct);
        if (previous != null)
        {
            var replay = await Load(business, previous.WorkOrderId, true, member, ct);
            if (previous.ActorUserId != actor.UserId || previous.Action != "create" || replay.AppointmentId != appointment || replay.CreationHash != hash)
                Fail("idempotencyConflict", "İstek anahtarı farklı işlem için kullanılmış.");
            return await View(replay, true, member, ct);
        }
        var a = await db.Appointments.Include(x => x.Reservations).SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == appointment, ct)
            ?? throw new SchedulingFault(404, "resourceNotFound", "Randevu bulunamadı.");
        RequireVersion(Version(a), input.AppointmentVersion);
        if (a.Status != AppointmentStatus.Confirmed) Fail("invalidStatus", "İptal edilmiş randevuya iş emri açılamaz.");
        if (await db.WorkOrders.AnyAsync(x => x.BusinessId == business && x.AppointmentId == appointment && x.CancelledAtUtc == null, ct))
            Fail("workOrderInUse", "Bu randevunun iş emri zaten var.");
        if (input.Stages.Sum(x => (long)x.Minutes) != a.ActiveMinutes) Fail("validation", "Aşama süreleri toplamı randevunun etkin süresine eşit olmalıdır.", 400);
        var employees = a.Reservations.Where(x => x.ReleasedAt == null && x.EmployeeMembershipId != null).Select(x => x.EmployeeMembershipId!.Value).ToArray();
        var order = WorkOrder.Create(business, appointment, input.Stages.Select(x => (x.Name, x.Minutes)).ToArray(), employees, actor.UserId, input.RequestId, clock.GetUtcNow(), hash);
        db.Add(order); await db.SaveChangesAsync(ct); return await View(order, true, member, ct);
    }, ct);
    private async Task CanStart(Business b, WorkOrder order, WorkOrderStage stage, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.BusinessId == b.Id && x.Id == order.AppointmentId, ct);
        var now = clock.GetUtcNow();
        if (now < a.StartsAtUtc) Fail("invalidStatus", "Planlanan başlangıç saatini bekleyin.");
        var start = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        var remaining = stage.StartedAtUtc == null ? stage.EstimatedMinutes * 60000L :
            stage.EstimatedMinutes * 60000L - (long)((stage.PausedAtUtc ?? now) - stage.StartedAtUtc.Value).TotalMilliseconds + stage.PausedMilliseconds;
        var requiredMinutes = Math.Max(1, (int)Math.Ceiling(remaining / 60000d));
        var end = start.AddMinutes(requiredMinutes + (now.Ticks % TimeSpan.TicksPerMinute == 0 ? 0 : 1));
        var interval = new Occupancy(start, end);
        var hours = await db.WeeklyOpeningDays.AsNoTracking().Where(x => x.BusinessId == b.Id).Select(x => new OpeningDay(x.Day, x.Closed, x.OpensAtMinute, x.ClosesAtMinute)).ToListAsync(ct);
        if (!WithinHours(interval, Zone(b.TimeZoneId), hours)) Fail("outsideHours", "Aşama işletme saatlerine sığmıyor.");
        var employees = stage.Assignments.Select(x => x.EmployeeMembershipId).Order().ToArray();
        foreach (var employee in employees)
        {
            var m = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {b.Id} AND \"Id\" = {employee} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (m == null || m.RevokedAt != null || !await db.BusinessMembershipRoles.AnyAsync(x => x.BusinessMembershipId == employee && x.Role == MembershipRole.Employee, ct))
                Fail("employeeUnavailable", "Etkin çalışan gerekli.");
        }
        var area = await db.WorkAreas.AsNoTracking().SingleAsync(x => x.BusinessId == b.Id && x.Id == a.WorkAreaId, ct);
        if (area.ArchivedAt != null) Fail("areaUnavailable", "Çalışma alanı arşivlenmiş.");
        if (!await WashFlow.Infrastructure.Scheduling.ResourceCapacity.Available(db, b.Id, area, interval, employees, a.Id, now, ct))
            Fail("resourceConflict", "Çalışan uygunluğu veya alan kapasitesi yetersiz.");
    }
    public Task<WorkOrderView> Act(SchedulingActor actor, Guid business, Guid id, Guid? stage, string action, WorkActionInput input, CancellationToken ct = default) => Run(actor, business, action == "cancel", async (b, member) =>
    {
        if (input.RequestId == Guid.Empty || input.Version == 0 || action is not ("start" or "pause" or "resume" or "complete" or "cancel")) Fail("validation", "Geçerli işlem ve sürüm gerekli.", 400);
        var owner = action == "cancel"; var order = await Load(business, id, owner, member, ct);
        if (!owner && !order.Stages.Any(x => x.Id == stage && x.Assignments.Any(y => y.EmployeeMembershipId == member))) Fail("forbidden", "Yalnızca atanan aşamalar güncellenebilir.", 403);
        var previous = await db.WorkOrderEvents.SingleOrDefaultAsync(x => x.BusinessId == business && x.RequestId == input.RequestId, ct);
        if (previous != null)
        {
            if (previous.WorkOrderId != id || previous.StageId != stage || previous.Action != action || previous.ActorUserId != actor.UserId) Fail("idempotencyConflict", "İstek anahtarı farklı işlem için kullanılmış.");
            return await View(order, owner, member, ct);
        }
        RequireVersion(Version(order), input.Version);
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(x => x.BusinessId == business && x.Id == order.AppointmentId, ct);
        if (appointment.Status != AppointmentStatus.Confirmed) Fail("invalidStatus", "Randevu iptal edilmiş.");
        var target = order.Stages.SingleOrDefault(x => x.Id == stage);
        if (action is "start" or "resume" && target != null) await CanStart(b, order, target, ct);
        try
        {
            if (owner) order.Cancel(actor.UserId, input.RequestId, clock.GetUtcNow());
            else order.Apply(stage!.Value, action, actor.UserId, input.RequestId, clock.GetUtcNow());
        }
        catch (InvalidOperationException) { throw new SchedulingFault(409, "invalidStatus", "Bu aşama durumu için işlem geçersiz."); }
        db.Entry(order).Property(x => x.UpdatedAt).IsModified = true;
        db.Entry(order.Events[^1]).State = EntityState.Added;
        await db.SaveChangesAsync(ct); return await View(order, owner, member, ct);
    }, ct);
}
