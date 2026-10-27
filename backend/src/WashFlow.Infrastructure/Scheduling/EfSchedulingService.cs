using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Businesses;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Scheduling;
using WashFlow.Infrastructure.Persistence;
using static WashFlow.Application.Scheduling.SchedulingRules;

namespace WashFlow.Infrastructure.Scheduling;

/// <summary>One transaction per operation; all authoritative reads follow the business lock.
/// Lock order: business, owner membership/grant, user/session, sorted employee membership/grants.
/// Uses READ COMMITTED so a waiter reads the winner's committed reservations after locking.</summary>
public sealed class EfSchedulingService(WashFlowDbContext db, TimeProvider clock) : ISchedulingService
{
    private static void Validate(Dictionary<string, string[]> errors)
    {
        if (errors.Count != 0) Fail("validation", string.Join(" ", errors.Values.SelectMany(x => x)), 400);
    }
    private uint Version<T>(T entity) where T : class => db.Entry(entity).Property<uint>("Version").CurrentValue;
    private void Expected<T>(T entity, uint expected) where T : class => RequireVersion(Version(entity), expected);
    private static T Found<T>(T? value) where T : class => value ?? throw new SchedulingFault(404, "resourceNotFound", "Kayıt bulunamadı.");

    private async Task<T> Execute<T>(SchedulingActor actor, Guid business, Func<Business, Task<T>> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Planlama işlemi bağımsız bir çalışma birimi gerektirir.");
        // A reused scoped service must not authorize or validate against EF's identity-map cache.
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var b = await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {business} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (b == null) Fail("forbidden", "İşletme sahibi yetkisi gereklidir.", 403);
            var m = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {business} AND \"UserId\" = {actor.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (m == null || m.RevokedAt != null) Fail("forbidden", "İşletme sahibi yetkisi gereklidir.", 403);
            var roles = await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {m.Id} AND \"Role\" = 1 FOR UPDATE").AsNoTracking().ToListAsync(ct);
            if (roles.Count != 1) Fail("forbidden", "İşletme sahibi yetkisi gereklidir.", 403);
            var user = await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var session = await db.DeviceSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {actor.SessionId} AND \"UserId\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var now = clock.GetUtcNow();
            if (user == null || session == null || session.RevokedAt != null || session.ExpiresAt <= now || session.CreatedAt > now || session.SecurityStamp != user.SecurityStamp)
                Fail("unauthorized", "Oturum geçersiz veya süresi dolmuş.", 401);
            var result = await operation(b);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SchedulingFault(409, "scheduleConflict", "Kayıt değişti. Güncel kaydı yeniden yükleyin.");
        }
        finally { db.ChangeTracker.Clear(); }
    }
    private Task<bool> Execute(SchedulingActor actor, Guid business, Func<Business, Task> operation, CancellationToken ct) =>
        Execute(actor, business, async b => { await operation(b); return true; }, ct);
    private IQueryable<AppointmentResourceReservation> Active(Guid b) => db.AppointmentResourceReservations.AsNoTracking()
        .Where(x => x.BusinessId == b && x.ReleasedAt == null);
    private IQueryable<Appointment> Aggregates(Guid b) => db.Appointments.Where(x => x.BusinessId == b)
        .Include(x => x.Events.OrderBy(e => e.Sequence)).Include(x => x.Reservations).AsSplitQuery();
    private async Task<Appointment> Load(Guid b, Guid id, CancellationToken ct) => Found(await Aggregates(b).SingleOrDefaultAsync(x => x.Id == id, ct));
    private AppointmentView View(Appointment a) => SchedulingValidation.View(a, Version(a));
    private WorkAreaView View(WorkArea a) => new(a.Id, a.BusinessId, a.Name, a.Capacity, a.ArchivedAt, Version(a));
    private AvailabilityView View(EmployeeAvailability a) => new(a.Id, a.BusinessId, a.MembershipId, a.StartsAtUtc, a.EndsAtUtc, a.ArchivedAt, Version(a));
    private RequirementView View(ServiceSchedulingRequirement a) => new(a.Id, a.BusinessId, a.ServiceId, a.Category, a.WorkAreaId, a.SingleStageConfirmed, Version(a));
    private async Task Employee(Guid b, Guid id, CancellationToken ct)
    {
        var m = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {b} AND \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (m == null || m.RevokedAt != null) Fail("employeeUnavailable", "Etkin çalışan bulunamadı.");
        var grant = await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {id} AND \"Role\" = 2 FOR UPDATE").AsNoTracking().ToListAsync(ct);
        if (grant.Count != 1) Fail("employeeUnavailable", "Etkin çalışan yetkisi gereklidir.");
    }
    private async Task<(BusinessService Service, ServiceOffering Offering, ServiceSchedulingRequirement Requirement, WorkArea Area)> Configuration(Guid b, Guid service, string category, CancellationToken ct)
    {
        var s = Found(await db.BusinessServices.SingleOrDefaultAsync(x => x.BusinessId == b && x.Id == service, ct));
        if (!s.Available) Fail("serviceUnavailable", "Hizmet kullanılamıyor.");
        var offering = Found(await db.ServiceOfferings.SingleOrDefaultAsync(x => x.ServiceId == service && x.Category == category, ct));
        var requirement = Found(await db.ServiceSchedulingRequirements.SingleOrDefaultAsync(x => x.BusinessId == b && x.ServiceId == service && x.Category == category, ct));
        var area = Found(await db.WorkAreas.SingleOrDefaultAsync(x => x.BusinessId == b && x.Id == requirement.WorkAreaId, ct));
        RequireWorkArea(b, service, category, requirement, area);
        return (s, offering, requirement, area);
    }
    private async Task<bool> Resources(Business b, WorkArea area, Occupancy interval, Guid[] employees, Guid? excluding, CancellationToken ct)
    {
        var hours = await db.WeeklyOpeningDays.AsNoTracking().Where(x => x.BusinessId == b.Id).OrderBy(x => x.Day)
            .Select(x => new OpeningDay(x.Day, x.Closed, x.OpensAtMinute, x.ClosesAtMinute)).ToListAsync(ct);
        if (!WithinHours(interval, Zone(b.TimeZoneId), hours)) Fail("outsideHours", "Randevu işletmenin açık olduğu saatlere sığmalıdır.");
        foreach (var employee in employees) await Employee(b.Id, employee, ct);
        var busy = await Active(b.Id).Where(x => x.AppointmentId != excluding && x.StartsAtUtc < interval.End && interval.Start < x.EndsAtUtc &&
            (x.WorkAreaId == area.Id || (x.EmployeeMembershipId != null && employees.Contains(x.EmployeeMembershipId.Value)))).ToListAsync(ct);
        if (!HasCapacity(busy.Where(x => x.WorkAreaId == area.Id).Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)), interval, area.Capacity)) return false;
        var windows = await db.EmployeeAvailabilities.AsNoTracking().Where(x => x.BusinessId == b.Id && x.ArchivedAt == null && employees.Contains(x.MembershipId) && x.StartsAtUtc < interval.End && interval.Start < x.EndsAtUtc).ToListAsync(ct);
        return employees.All(e => EmployeeAvailable(windows.Where(x => x.MembershipId == e).Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)),
            busy.Where(x => x.EmployeeMembershipId == e).Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)), interval));
    }
    private static Occupancy Interval(DateTimeOffset start, int minutes)
    {
        try { return new(start, start.AddMinutes(minutes)); }
        catch (ArgumentException) { throw new SchedulingFault(400, "invalidInterval", "Randevu tarih aralığı geçersiz."); }
    }
    public Task<WorkAreaView> SaveWorkArea(SchedulingActor actor, Guid business, Guid? id, WorkAreaInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        Validate(SchedulingValidation.WorkArea(input, id != null));
        var now = clock.GetUtcNow();
        var area = id == null ? WorkArea.Create(business, input.Name!, input.Capacity, now) : Found(await db.WorkAreas.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == id && x.ArchivedAt == null, ct));
        if (id != null)
        {
            Expected(area, input.Version!.Value);
            var busy = await Active(business).Where(x => x.WorkAreaId == id && x.EndsAtUtc > now).ToListAsync(ct);
            if (Peak(busy.Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc))) > input.Capacity) Fail("capacityInUse", "Kapasite mevcut randevuların altına düşürülemez.");
            area.Update(input.Name!, input.Capacity, now);
            db.Entry(area).Property(x => x.UpdatedAt).IsModified = true;
        }
        else db.Add(area);
        await db.SaveChangesAsync(ct); return View(area);
    }, ct);
    public Task ArchiveWorkArea(SchedulingActor actor, Guid business, Guid id, VersionInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        Validate(SchedulingValidation.Versioned(input));
        var area = Found(await db.WorkAreas.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == id && x.ArchivedAt == null, ct)); Expected(area, input.Version);
        if (await Active(business).AnyAsync(x => x.WorkAreaId == id && x.EndsAtUtc > clock.GetUtcNow(), ct)) Fail("areaInUse", "Planlanmış randevuları olan alan arşivlenemez.");
        area.Archive(clock.GetUtcNow()); await db.SaveChangesAsync(ct);
    }, ct);
    public Task<RequirementView> SaveRequirement(SchedulingActor actor, Guid business, Guid service, string category, RequirementInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        var row = await db.ServiceSchedulingRequirements.SingleOrDefaultAsync(x => x.BusinessId == business && x.ServiceId == service && x.Category == category, ct);
        Validate(SchedulingValidation.Requirement(input, category, row != null));
        var s = Found(await db.BusinessServices.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == service, ct));
        if (!s.Available || !await db.ServiceOfferings.AnyAsync(x => x.ServiceId == service && x.Category == category, ct)) Fail("serviceUnavailable", "Hizmet ve araç türü sunumu etkin olmalıdır.");
        Found(await db.WorkAreas.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == input.WorkAreaId && x.ArchivedAt == null, ct));
        if (row == null) { row = ServiceSchedulingRequirement.Create(business, service, category, input.WorkAreaId, input.SingleStageConfirmed, clock.GetUtcNow()); db.Add(row); }
        else
        {
            Expected(row, input.Version!.Value);
            if ((row.WorkAreaId != input.WorkAreaId || row.SingleStageConfirmed != input.SingleStageConfirmed) && await db.Appointments.AnyAsync(x => x.BusinessId == business && x.ServiceId == service && x.Category == category && x.Status == AppointmentStatus.Confirmed && x.ActiveEndsAtUtc > clock.GetUtcNow(), ct))
                Fail("requirementInUse", "Planlanmış randevular varken kaynak gereksinimi değiştirilemez.");
            row.Update(input.WorkAreaId, input.SingleStageConfirmed, clock.GetUtcNow()); db.Entry(row).Property(x => x.UpdatedAt).IsModified = true;
        }
        await db.SaveChangesAsync(ct); return View(row);
    }, ct);
    private async Task PreserveCoverage(Guid business, Guid employee, Guid changed, Occupancy? replacement, CancellationToken ct)
    {
        var remaining = await db.EmployeeAvailabilities.AsNoTracking().Where(x => x.BusinessId == business && x.MembershipId == employee && x.Id != changed && x.ArchivedAt == null).ToListAsync(ct);
        var windows = remaining.Select(x => new Occupancy(x.StartsAtUtc, x.EndsAtUtc)).ToList(); if (replacement is { } r) windows.Add(r);
        var reservations = await Active(business).Where(x => x.EmployeeMembershipId == employee && x.EndsAtUtc > clock.GetUtcNow()).ToListAsync(ct);
        if (reservations.Any(x => !Covered(windows, new(x.StartsAtUtc, x.EndsAtUtc)))) Fail("availabilityInUse", "Uygunluk değişikliği mevcut randevuları kapsam dışında bırakamaz.");
    }
    public Task<AvailabilityView> SaveAvailability(SchedulingActor actor, Guid business, Guid employee, Guid? id, AvailabilityInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        Validate(SchedulingValidation.Availability(input, id != null)); await Employee(business, employee, ct);
        var window = new Occupancy(input.StartsAtUtc, input.EndsAtUtc);
        var row = id == null ? EmployeeAvailability.Create(business, employee, window, clock.GetUtcNow()) : Found(await db.EmployeeAvailabilities.SingleOrDefaultAsync(x => x.BusinessId == business && x.MembershipId == employee && x.Id == id && x.ArchivedAt == null, ct));
        if (id == null) db.Add(row);
        else { Expected(row, input.Version!.Value); await PreserveCoverage(business, employee, row.Id, window, ct); row.Update(window, clock.GetUtcNow()); db.Entry(row).Property(x => x.UpdatedAt).IsModified = true; }
        await db.SaveChangesAsync(ct); return View(row);
    }, ct);
    public Task ArchiveAvailability(SchedulingActor actor, Guid business, Guid employee, Guid id, VersionInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        Validate(SchedulingValidation.Versioned(input));
        var row = Found(await db.EmployeeAvailabilities.SingleOrDefaultAsync(x => x.BusinessId == business && x.MembershipId == employee && x.Id == id && x.ArchivedAt == null, ct));
        Expected(row, input.Version); await PreserveCoverage(business, employee, id, null, ct); row.Archive(clock.GetUtcNow()); await db.SaveChangesAsync(ct);
    }, ct);
    public Task<IReadOnlyList<WorkAreaView>> WorkAreas(SchedulingActor actor, Guid business, CancellationToken ct = default) => Execute<IReadOnlyList<WorkAreaView>>(actor, business, async _ =>
        (await db.WorkAreas.Where(x => x.BusinessId == business).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct)).Select(View).ToArray(), ct);
    public Task<IReadOnlyList<RequirementView>> Requirements(SchedulingActor actor, Guid business, CancellationToken ct = default) => Execute<IReadOnlyList<RequirementView>>(actor, business, async _ =>
        (await db.ServiceSchedulingRequirements.Where(x => x.BusinessId == business).OrderBy(x => x.ServiceId).ThenBy(x => x.Category).ToListAsync(ct)).Select(View).ToArray(), ct);
    public Task<IReadOnlyList<AvailabilityView>> Availability(SchedulingActor actor, Guid business, Guid employee, CancellationToken ct = default) => Execute<IReadOnlyList<AvailabilityView>>(actor, business, async _ =>
        (await db.EmployeeAvailabilities.Where(x => x.BusinessId == business && x.MembershipId == employee).OrderBy(x => x.StartsAtUtc).ToListAsync(ct)).Select(View).ToArray(), ct);
    public Task<IReadOnlyList<EmployeeView>> Employees(SchedulingActor actor, Guid business, CancellationToken ct = default) => Execute<IReadOnlyList<EmployeeView>>(actor, business, async _ =>
        await (from m in db.BusinessMemberships join g in db.BusinessMembershipRoles on m.Id equals g.BusinessMembershipId join u in db.Users on m.UserId equals u.Id
               where m.BusinessId == business && m.RevokedAt == null && g.Role == MembershipRole.Employee orderby m.Id select new EmployeeView(m.Id, business, u.FullName)).ToListAsync(ct), ct);

    public Task<SchedulingResult<AppointmentView>> Create(SchedulingActor actor, Guid business, BookingInput input, CancellationToken ct = default) => Execute(actor, business, async b =>
    {
        Validate(SchedulingValidation.Booking(input));
        // Retry lookup precedes mutable configuration/ownership/version checks.
        var existing = await db.Appointments.SingleOrDefaultAsync(x => x.BusinessId == business && x.ClientRequestId == input.ClientRequestId, ct);
        if (existing != null)
        {
            if (existing.RequestHash != SchedulingValidation.RequestHash(input, Zone(existing.TimeZoneIdSnapshot))) Fail("idempotencyConflict", "İstek anahtarı farklı bir randevu isteği için kullanılmış.");
            var receipt = await db.SchedulingCreateReceipts.SingleAsync(x => x.BusinessId == business && x.ClientRequestId == input.ClientRequestId && x.AppointmentId == existing.Id, ct);
            return new SchedulingResult<AppointmentView>(JsonSerializer.Deserialize<AppointmentView>(receipt.ResultJson)!, false);
        }
        var customer = Found(await db.LocalCustomers.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == input.LocalCustomerId, ct));
        var vehicle = Found(await db.LocalVehicles.SingleOrDefaultAsync(x => x.BusinessId == business && x.LocalCustomerId == customer.Id && x.Id == input.LocalVehicleId, ct));
        var config = await Configuration(business, input.ServiceId, vehicle.Category, ct);
        Expected(config.Service, input.ExpectedServiceVersion); Expected(vehicle, input.ExpectedVehicleVersion);
        var zone = Zone(b.TimeZoneId); var snapshot = SchedulingValidation.CreateSnapshot(config.Service, config.Offering, customer, vehicle, zone.Id);
        var start = Resolve(input.StartLocal, input.UtcOffsetMinutes, zone); var interval = Interval(start, snapshot.ActiveMinutes);
        var employees = SchedulingRules.Employees(input.EmployeeMembershipIds, snapshot.Employees);
        if (!await Resources(b, config.Area, interval, employees, null, ct)) Fail("resourceConflict", "Çalışan uygunluğu veya çalışma alanı kapasitesi yetersiz.");
        var row = Appointment.Create(business, customer.Id, vehicle.Id, config.Service.Id, config.Area.Id, actor.UserId, input.ClientRequestId,
            SchedulingValidation.RequestHash(input, zone), BookingSource.OwnerPhone, snapshot, start, employees, clock.GetUtcNow());
        db.Add(row); await db.SaveChangesAsync(ct);
        var view = View(row);
        db.Add(new SchedulingCreateReceipt { BusinessId = business, ClientRequestId = input.ClientRequestId, AppointmentId = row.Id, ResultJson = JsonSerializer.Serialize(view) });
        await db.SaveChangesAsync(ct);
        return new SchedulingResult<AppointmentView>(view, true);
    }, ct);
    public Task<AppointmentView> Reschedule(SchedulingActor actor, Guid business, Guid id, RescheduleInput input, CancellationToken ct = default) => Execute(actor, business, async b =>
    {
        Validate(SchedulingValidation.Reschedule(input)); var row = await Load(business, id, ct); Expected(row, input.Version);
        if (row.Status != AppointmentStatus.Confirmed) Fail("invalidStatus", "Yalnızca onaylı randevu yeniden planlanabilir.");
        var config = await Configuration(business, row.ServiceId, row.Category, ct);
        if (config.Area.Id != row.WorkAreaId) Fail("requirementChanged", "Randevunun kaynak gereksinimi değişmiş.");
        _ = Found(await db.LocalCustomers.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == row.LocalCustomerId, ct));
        _ = Found(await db.LocalVehicles.SingleOrDefaultAsync(x => x.BusinessId == business && x.LocalCustomerId == row.LocalCustomerId && x.Id == row.LocalVehicleId && x.ArchivedAt == null && x.Category == row.Category, ct));
        // Rescheduling preserves agreed duration/price/staff snapshots; current service must still be available.
        var start = Resolve(input.StartLocal, input.UtcOffsetMinutes, Zone(b.TimeZoneId)); var interval = Interval(start, row.ActiveMinutes);
        var employees = SchedulingRules.Employees(input.EmployeeMembershipIds, row.Employees);
        if (!await Resources(b, config.Area, interval, employees, row.Id, ct)) Fail("resourceConflict", "Çalışan uygunluğu veya çalışma alanı kapasitesi yetersiz.");
        // Flush releases before inserting replacements to satisfy active-resource unique indexes.
        var now = clock.GetUtcNow(); foreach (var r in row.Reservations.Where(x => x.ReleasedAt == null)) r.Release(now);
        await db.SaveChangesAsync(ct);
        row.Reschedule(start, employees, actor.UserId, now);
        db.Entry(row).Property(x => x.UpdatedAt).IsModified = true;
        // Newly appended aggregate children have assigned IDs; explicitly mark Added, never Modified.
        foreach (var r in row.Reservations.Where(x => x.ReleasedAt == null)) db.Entry(r).State = EntityState.Added;
        db.Entry(row.Events[^1]).State = EntityState.Added;
        await db.SaveChangesAsync(ct); return View(row);
    }, ct);
    public Task<AppointmentView> Cancel(SchedulingActor actor, Guid business, Guid id, VersionInput input, CancellationToken ct = default) => Execute(actor, business, async _ =>
    {
        Validate(SchedulingValidation.Versioned(input)); var row = await Load(business, id, ct); Expected(row, input.Version);
        if (row.Status != AppointmentStatus.Confirmed) Fail("invalidStatus", "Randevu zaten iptal edilmiş.");
        row.Cancel(actor.UserId, clock.GetUtcNow()); db.Entry(row).Property(x => x.UpdatedAt).IsModified = true;
        db.Entry(row.Events[^1]).State = EntityState.Added;
        await db.SaveChangesAsync(ct); return View(row);
    }, ct);
    public Task<AppointmentView> Detail(SchedulingActor actor, Guid business, Guid id, CancellationToken ct = default) => Execute(actor, business, async _ => View(await Load(business, id, ct)), ct);
    public Task<IReadOnlyList<AppointmentView>> Calendar(SchedulingActor actor, Guid business, CalendarInput input, CancellationToken ct = default) => Execute<IReadOnlyList<AppointmentView>>(actor, business, async b =>
    {
        Validate(SchedulingValidation.Calendar(input)); var zone = Zone(b.TimeZoneId);
        var start = Resolve(input.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00", null, zone);
        var end = Resolve(input.ToDateExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00", null, zone);
        var q = Aggregates(business).Where(x => x.StartsAtUtc < end && x.ActiveEndsAtUtc > start && (input.Status == null || x.Status == input.Status));
        return (await q.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToListAsync(ct)).Select(View).ToArray();
    }, ct);
    public Task<AvailabilityCheckView> Preview(SchedulingActor actor, Guid business, AvailabilityCheckInput input, CancellationToken ct = default) => Execute(actor, business, async b =>
    {
        Validate(SchedulingValidation.AvailabilityCheck(input)); var config = await Configuration(business, input.ServiceId, input.Category, ct);
        var interval = Interval(Resolve(input.StartLocal, input.UtcOffsetMinutes, Zone(b.TimeZoneId)), config.Offering.ActiveMinutes);
        var available = await Resources(b, config.Area, interval, SchedulingRules.Employees(input.EmployeeMembershipIds, config.Offering.Employees), null, ct);
        return new AvailabilityCheckView(available, interval.Start, interval.End);
    }, ct);
}
