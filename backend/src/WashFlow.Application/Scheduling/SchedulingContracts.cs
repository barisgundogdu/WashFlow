using WashFlow.Application.Businesses;
using WashFlow.Application.Customers;

namespace WashFlow.Application.Scheduling;

public sealed class SchedulingFault(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record SchedulingActor(Guid UserId, Guid SessionId);
public sealed record WorkAreaInput(string? Name, int Capacity, uint? Version);
public sealed record AvailabilityInput(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, uint? Version);
public sealed record RequirementInput(Guid WorkAreaId, bool SingleStageConfirmed, uint? Version);
public sealed record VersionInput(uint Version);
public sealed record BookingInput(Guid ClientRequestId, Guid LocalCustomerId, Guid LocalVehicleId, Guid ServiceId,
    uint ExpectedServiceVersion, uint ExpectedVehicleVersion, string? StartLocal, int? UtcOffsetMinutes,
    IReadOnlyList<Guid>? EmployeeMembershipIds, string Source = "OwnerPhone");
public sealed record RescheduleInput(uint Version, string? StartLocal, int? UtcOffsetMinutes, IReadOnlyList<Guid>? EmployeeMembershipIds);
public sealed record SchedulingResult(object? Value, bool Created = false);
public interface ISchedulingStore
{
    Task<SchedulingResult> Execute(SchedulingActor actor, Guid business, string operation, Guid? id,
        Guid? parent, object? input, string? from, string? to, int page, int size, CancellationToken ct);
}

public readonly record struct Occupancy(DateTimeOffset Start, DateTimeOffset End);
public static class SchedulingRules
{
    public const int HorizonDays = 180;
    public static void Fail(string code, string message, int status = 409) => throw new SchedulingFault(status, code, message);
    public static bool Overlaps(Occupancy a, Occupancy b) => a.Start < b.End && b.Start < a.End;
    public static int Peak(IEnumerable<Occupancy> existing, Occupancy? candidate = null)
    {
        var intervals = candidate is { } c ? existing.Where(x => Overlaps(x, c)).Select(x => new Occupancy(x.Start < c.Start ? c.Start : x.Start, x.End > c.End ? c.End : x.End)).Append(c) : existing;
        var events = intervals.SelectMany(x => new[] { (Time: x.Start, Delta: 1), (Time: x.End, Delta: -1) }).OrderBy(x => x.Time).ThenBy(x => x.Delta);
        var current = 0; var peak = 0;
        foreach (var e in events) { current += e.Delta; peak = Math.Max(current, peak); }
        return peak;
    }
    public static bool Covered(IEnumerable<Occupancy> windows, Occupancy interval)
    {
        var end = interval.Start;
        foreach (var window in windows.OrderBy(x => x.Start))
        {
            if (window.Start > end) break;
            if (window.End > end) end = window.End;
            if (end >= interval.End) return true;
        }
        return false;
    }
    public static TimeZoneInfo Zone(string? id)
    {
        if (id == null || !BusinessSettingsValidation.ValidTimeZone(id)) Fail("hoursUnconfigured", "İşletmenin saat dilimini ve çalışma saatlerini yapılandırın.");
        return TimeZoneInfo.FindSystemTimeZoneById(id!);
    }
    public static DateTimeOffset Resolve(string? local, int? offset, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact(local, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var wall)) Fail("invalidLocalTime", "Tarih ve saati dakika hassasiyetinde girin.", 400);
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) Fail("invalidLocalTime", "Bu yerel saat saat dilimi geçişi nedeniyle mevcut değil.", 400);
        var offsets = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall) : [zone.GetUtcOffset(wall)];
        if (offset == null && offsets.Length > 1) Fail("ambiguousLocalTime", "Bu saat iki kez oluşuyor. UTC farkını dakika olarak belirtin.", 400);
        var selected = offset == null ? offsets[0] : TimeSpan.FromMinutes(offset.Value);
        if (!offsets.Contains(selected)) Fail("invalidLocalTime", "UTC farkı işletmenin saat dilimine uymuyor.", 400);
        return new DateTimeOffset(wall, selected).ToUniversalTime();
    }
    public static bool WithinHours(Occupancy interval, TimeZoneInfo zone, IReadOnlyList<OpeningDay>? hours)
    {
        if (hours == null) return false;
        var start = TimeZoneInfo.ConvertTime(interval.Start, zone);
        var day = hours.SingleOrDefault(x => x.Day == ((int)start.DayOfWeek + 6) % 7 + 1);
        if (day == null || day.Closed || day.OpensAtMinute == null || day.ClosesAtMinute == null) return false;
        var open = start.Date.AddMinutes(day.OpensAtMinute.Value);
        var close = start.Date.AddMinutes(day.ClosesAtMinute.Value);
        // Conservatively reject transition-boundary schedules; never pick an offset silently.
        if (zone.IsInvalidTime(open) || zone.IsAmbiguousTime(open) || zone.IsInvalidTime(close) || zone.IsAmbiguousTime(close)) return false;
        var opensAt = new DateTimeOffset(open, zone.GetUtcOffset(open)).ToUniversalTime();
        var closesAt = new DateTimeOffset(close, zone.GetUtcOffset(close)).ToUniversalTime();
        return interval.Start >= opensAt && interval.End <= closesAt;
    }
    public static Guid[] Employees(IReadOnlyList<Guid>? ids, int count)
    {
        if (ids == null || ids.Count != count || ids.Count > 100 || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            Fail("invalidEmployees", $"Tam olarak {count} farklı çalışan seçin.", 400);
        return ids!.OrderBy(x => x).ToArray();
    }
}
