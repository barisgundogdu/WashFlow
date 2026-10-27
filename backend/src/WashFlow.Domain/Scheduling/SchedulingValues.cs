using System.Text;

namespace WashFlow.Domain.Scheduling;

public enum AppointmentStatus { Confirmed = 1, Cancelled = 2 }
// Only explicitly approved sources are accepted. Customer and walk-in flows are deferred.
public enum BookingSource { OwnerPhone = 1 }
public enum AppointmentEventKind { Created = 1, Rescheduled = 2, Cancelled = 3 }

/// <summary>UTC, minute-precision, half-open interval. Validate also rejects default(Occupancy).</summary>
public readonly record struct Occupancy
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public Occupancy(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start.ToUniversalTime(); End = end.ToUniversalTime();
        Validate();
    }
    public void Validate()
    {
        SchedulingInvariant.Require(Start < End, "Başlangıç bitişten önce olmalıdır.");
        SchedulingInvariant.Require(Start.Ticks % TimeSpan.TicksPerMinute == 0 && End.Ticks % TimeSpan.TicksPerMinute == 0,
            "Zamanlar dakika hassasiyetinde olmalıdır.");
    }
}

/// <summary>Immutable agreed values, independent of mutable/replaced catalog offering rows.</summary>
public sealed class AppointmentSnapshot
{
    public string ServiceName { get; }
    public string CustomerName { get; }
    public string VehiclePlate { get; }
    public string Category { get; }
    public decimal Price { get; }
    public string Currency { get; }
    public int ActiveMinutes { get; }
    public int? WaitingMinutes { get; }
    public int Employees { get; }
    public string TimeZoneId { get; }

    public AppointmentSnapshot(string serviceName, string customerName, string vehiclePlate, string category,
        decimal price, string currency, int activeMinutes, int? waitingMinutes, int employees, string timeZoneId)
    {
        ServiceName = SchedulingInvariant.Text(serviceName, 200);
        CustomerName = SchedulingInvariant.Text(customerName, 200);
        VehiclePlate = SchedulingInvariant.Text(vehiclePlate, 32);
        Category = SchedulingInvariant.Text(category, 32);
        TimeZoneId = SchedulingInvariant.Text(timeZoneId, 100);
        SchedulingInvariant.Require(price is >= 0 and <= 9999999.99m && decimal.Round(price, 2) == price,
            "Fiyat 0–9.999.999,99 TL ve en fazla iki ondalık basamak olmalıdır.");
        SchedulingInvariant.Require(currency == "TRY", "Yalnızca TRY desteklenir.");
        SchedulingInvariant.Require(activeMinutes is >= 1 and <= 10080, "Etkin süre 1–10080 dakika olmalıdır.");
        SchedulingInvariant.Require(waitingMinutes is null or >= 0 and <= 43200, "Bekleme 0–43200 dakika olmalıdır.");
        SchedulingInvariant.Require(employees is >= 1 and <= 100, "Çalışan sayısı 1–100 olmalıdır.");
        Price = price; Currency = currency; ActiveMinutes = activeMinutes; WaitingMinutes = waitingMinutes; Employees = employees;
    }
}

internal static class SchedulingInvariant
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
    public static void Id(Guid id) => Require(id != Guid.Empty, "Geçerli kayıt kimliği gereklidir.");
    public static string Text(string? value, int maximum)
    {
        Require(value != null && !value.Any(c => char.IsSurrogate(c) || char.IsControl(c)), "Desteklenmeyen karakter içeriyor.");
        var normalized = value!.Trim().Normalize(NormalizationForm.FormC);
        Require(normalized.Length >= 1 && normalized.Length <= maximum, $"Alan 1–{maximum} karakter olmalıdır.");
        return normalized;
    }
    public static DateTimeOffset ChangeTime(DateTimeOffset now, DateTimeOffset previous)
    {
        now = now.ToUniversalTime();
        Require(now >= previous, "Değişiklik zamanı önceki kayıttan önce olamaz.");
        return now;
    }
    public static Guid[] Employees(IEnumerable<Guid> employees, int required)
    {
        ArgumentNullException.ThrowIfNull(employees);
        var ids = employees.Take(101).ToArray();
        Require(required is >= 1 and <= 100 && ids.Length == required && ids.All(x => x != Guid.Empty) && ids.Distinct().Count() == required,
            "Gerekli sayıda farklı çalışan seçin.");
        return ids.OrderBy(x => x).ToArray();
    }
}
