using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using WashFlow.Application.Businesses;
using WashFlow.Domain.Scheduling;

namespace WashFlow.Application.Scheduling;

/// <summary>Pure calculations over supplied data. Does not establish authorization or database concurrency.</summary>
public static class SchedulingRules
{
    public const int HorizonDays = 180;
    [DoesNotReturn]
    public static void Fail(string code, string message, int status = 409) => throw new SchedulingFault(status, code, message);
    public static bool Overlaps(Occupancy a, Occupancy b)
    {
        a.Validate(); b.Validate();
        return a.Start < b.End && b.Start < a.End;
    }
    public static int Peak(IEnumerable<Occupancy> existing, Occupancy? candidate = null)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var intervals = existing.ToArray();
        foreach (var interval in intervals) interval.Validate();
        IEnumerable<Occupancy> relevant = intervals;
        if (candidate is { } c)
        {
            c.Validate();
            relevant = intervals.Where(x => Overlaps(x, c))
                .Select(x => new Occupancy(x.Start < c.Start ? c.Start : x.Start, x.End > c.End ? c.End : x.End)).Append(c);
        }
        var events = relevant.SelectMany(x => new[] { (Time: x.Start, Delta: 1), (Time: x.End, Delta: -1) })
            .OrderBy(x => x.Time).ThenBy(x => x.Delta);
        var current = 0; var peak = 0;
        foreach (var e in events) { current = checked(current + e.Delta); peak = Math.Max(current, peak); }
        return peak;
    }
    public static bool HasCapacity(IEnumerable<Occupancy> existing, Occupancy candidate, int capacity)
    {
        if (capacity <= 0) Fail("invalidCapacity", "Kapasite pozitif olmalıdır.", 400);
        return Peak(existing, candidate) <= capacity;
    }
    public static bool Covered(IEnumerable<Occupancy> windows, Occupancy interval)
    {
        ArgumentNullException.ThrowIfNull(windows); interval.Validate();
        var ordered = windows.ToArray();
        foreach (var window in ordered) window.Validate();
        var end = interval.Start;
        foreach (var window in ordered.OrderBy(x => x.Start))
        {
            if (window.Start > end) break;
            if (window.End > end) end = window.End;
            if (end >= interval.End) return true;
        }
        return false;
    }
    public static bool EmployeeAvailable(IEnumerable<Occupancy> windows, IEnumerable<Occupancy> reservations, Occupancy requested)
    {
        ArgumentNullException.ThrowIfNull(reservations); requested.Validate();
        var busy = reservations.ToArray();
        foreach (var interval in busy) interval.Validate();
        return Covered(windows, requested) && !busy.Any(x => Overlaps(x, requested));
    }
    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !BusinessSettingsValidation.ValidTimeZone(id))
            Fail("hoursUnconfigured", "Geçerli bir IANA işletme saat dilimi yapılandırın.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new SchedulingFault(400, "invalidTimeZone", "Saat dilimi sunucuda desteklenmiyor."); }
    }
    public static DateTimeOffset Resolve(string? local, int? offset, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!DateTime.TryParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            Fail("invalidLocalTime", "Tarih ve saati dakika hassasiyetinde girin.", 400);
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) Fail("invalidLocalTime", "Bu yerel saat saat dilimi geçişi nedeniyle mevcut değil.", 400);
        var offsets = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall) : [zone.GetUtcOffset(wall)];
        if (offset == null && offsets.Length > 1) Fail("ambiguousLocalTime", "Bu saat iki kez oluşuyor. UTC farkını dakika olarak belirtin.", 400);
        var selected = offset == null ? offsets[0] : TimeSpan.FromMinutes(offset.Value);
        if (!offsets.Contains(selected)) Fail("invalidLocalTime", "UTC farkı işletmenin saat dilimine uymuyor.", 400);
        try { return new DateTimeOffset(wall, selected).ToUniversalTime(); }
        catch (ArgumentException) { throw new SchedulingFault(400, "invalidLocalTime", "Yerel tarih desteklenen UTC aralığının dışında."); }
    }
    public static bool WithinHours(Occupancy interval, TimeZoneInfo zone, IReadOnlyList<OpeningDay>? hours)
    {
        interval.Validate(); ArgumentNullException.ThrowIfNull(zone);
        if (hours == null) return false;
        // Reuse Phase 6's complete weekly-hours contract; malformed patterns fail closed.
        // Validate only the pattern here; the already-resolved zone may be a deterministic test zone.
        var settings = new BusinessSettingsInput { Name = "Saat doğrulama", Version = 1, TimeZoneId = "Etc/UTC", WeeklyHours = hours };
        if (BusinessSettingsValidation.Validate(settings).Count != 0) return false;
        var start = TimeZoneInfo.ConvertTime(interval.Start, zone);
        var day = hours.Single(x => x.Day == ((int)start.DayOfWeek + 6) % 7 + 1);
        if (day.Closed) return false;
        try
        {
            var open = start.Date.AddMinutes(day.OpensAtMinute!.Value);
            var close = start.Date.AddMinutes(day.ClosesAtMinute!.Value);
            // Never silently choose a DST occurrence for an opening/closing boundary.
            if (zone.IsInvalidTime(open) || zone.IsAmbiguousTime(open) || zone.IsInvalidTime(close) || zone.IsAmbiguousTime(close)) return false;
            var opensAt = new DateTimeOffset(open, zone.GetUtcOffset(open)).ToUniversalTime();
            var closesAt = new DateTimeOffset(close, zone.GetUtcOffset(close)).ToUniversalTime();
            return interval.Start >= opensAt && interval.End <= closesAt;
        }
        catch (ArgumentException) { return false; }
    }
    public static Guid[] Employees(IReadOnlyList<Guid>? ids, int count)
    {
        if (count is < 1 or > 100 || ids == null || ids.Count != count || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            Fail("invalidEmployees", "Gerekli sayıda farklı çalışan seçin.", 400);
        return ids.OrderBy(x => x).ToArray();
    }
    public static void RequireSingleStage(ServiceSchedulingRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (!requirement.SingleStageConfirmed) Fail("unsupportedStages", "Bu hizmet için tek etkin aşama açıkça onaylanmalıdır.");
    }
    public static void RequireWorkArea(Guid businessId, Guid serviceId, string category,
        ServiceSchedulingRequirement requirement, WorkArea area)
    {
        ArgumentNullException.ThrowIfNull(requirement); ArgumentNullException.ThrowIfNull(area);
        if (businessId == Guid.Empty || requirement.BusinessId != businessId || area.BusinessId != businessId ||
            requirement.ServiceId != serviceId || requirement.Category != category || requirement.WorkAreaId != area.Id)
            Fail("resourceNotFound", "Hizmet ve çalışma alanı yapılandırması bulunamadı.", 404);
        RequireSingleStage(requirement);
        if (area.ArchivedAt != null) Fail("areaUnavailable", "Çalışma alanı arşivlenmiş.");
    }
    public static void RequireVersion(uint actual, uint expected)
    {
        if (expected == 0) Fail("invalidVersion", "Güncel kayıt sürümü gereklidir.", 400);
        if (actual == 0 || actual != expected) Fail("scheduleConflict", "Kayıt değişti. Güncel kaydı yeniden yükleyin.");
    }
}
