using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WashFlow.Application.Catalog;
using WashFlow.Application.Customers;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Scheduling;

namespace WashFlow.Application.Scheduling;

public static class SchedulingValidation
{
    private static void Version(Dictionary<string, string[]> errors, uint? version, bool update)
    {
        if (update && version is null or 0) errors["version"] = ["Güncel kayıt sürümü gereklidir."];
    }
    private static void EmployeeIds(Dictionary<string, string[]> errors, IReadOnlyList<Guid>? ids)
    {
        if (ids == null || ids.Count is < 1 or > 100 || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            errors["employeeMembershipIds"] = ["1–100 farklı çalışan seçin."];
    }
    private static void LocalTime(Dictionary<string, string[]> errors, string? local, int? offset)
    {
        if (!DateTime.TryParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            errors["startLocal"] = ["Yerel tarihi yyyy-MM-ddTHH:mm biçiminde girin."];
        if (offset is < -840 or > 840) errors["utcOffsetMinutes"] = ["UTC farkı -840–840 dakika arasında olmalıdır."];
    }
    public static Dictionary<string, string[]> WorkArea(WorkAreaInput input, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        // Reuse the catalog's normalized text policy instead of introducing a second policy.
        var text = CatalogValidation.Validate(new ServiceInput { Name = input.Name, Offerings = [] }, false);
        if (text.TryGetValue("name", out var message)) errors["name"] = message;
        if (input.Capacity <= 0) errors["capacity"] = ["Kapasite pozitif olmalıdır."];
        Version(errors, input.Version, update); return errors;
    }
    public static Dictionary<string, string[]> Availability(AvailabilityInput input, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        try { _ = new Occupancy(input.StartsAtUtc, input.EndsAtUtc); }
        catch (ArgumentException) { errors["interval"] = ["Başlangıç bitişten önce ve zamanlar dakika hassasiyetinde olmalıdır."]; }
        Version(errors, input.Version, update); return errors;
    }
    public static Dictionary<string, string[]> Requirement(RequirementInput input, string? category, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.WorkAreaId == Guid.Empty) errors["workAreaId"] = ["Çalışma alanı seçin."];
        if (category == null || !CustomerVehicleValidation.Categories.Contains(category)) errors["category"] = ["Geçerli araç türü seçin."];
        Version(errors, input.Version, update); return errors;
    }
    public static Dictionary<string, string[]> Booking(BookingInput input)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, id) in new[] { ("clientRequestId", input.ClientRequestId), ("localCustomerId", input.LocalCustomerId),
            ("localVehicleId", input.LocalVehicleId), ("serviceId", input.ServiceId) })
            if (id == Guid.Empty) errors[key] = ["Geçerli kayıt kimliği gereklidir."];
        if (input.ExpectedServiceVersion == 0) errors["expectedServiceVersion"] = ["Güncel hizmet sürümü gereklidir."];
        if (input.ExpectedVehicleVersion == 0) errors["expectedVehicleVersion"] = ["Güncel araç sürümü gereklidir."];
        if (input.Source != nameof(BookingSource.OwnerPhone)) errors["source"] = ["Yalnızca onaylı OwnerPhone kaynağı desteklenir."];
        LocalTime(errors, input.StartLocal, input.UtcOffsetMinutes); EmployeeIds(errors, input.EmployeeMembershipIds);
        return errors;
    }
    public static Dictionary<string, string[]> Reschedule(RescheduleInput input)
    {
        var errors = new Dictionary<string, string[]>(); Version(errors, input.Version, true);
        LocalTime(errors, input.StartLocal, input.UtcOffsetMinutes); EmployeeIds(errors, input.EmployeeMembershipIds);
        return errors;
    }
    public static Dictionary<string, string[]> Versioned(VersionInput input)
    {
        var errors = new Dictionary<string, string[]>(); Version(errors, input.Version, true); return errors;
    }
    public static Dictionary<string, string[]> AvailabilityCheck(AvailabilityCheckInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.ServiceId == Guid.Empty) errors["serviceId"] = ["Hizmet seçin."];
        if (!CustomerVehicleValidation.Categories.Contains(input.Category)) errors["category"] = ["Geçerli araç türü seçin."];
        LocalTime(errors, input.StartLocal, input.UtcOffsetMinutes); EmployeeIds(errors, input.EmployeeMembershipIds);
        return errors;
    }
    public static Dictionary<string, string[]> AvailabilityRange(AvailabilityRangeInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.ToUtcExclusive <= input.FromUtc || input.ToUtcExclusive - input.FromUtc > TimeSpan.FromDays(31))
            errors["dateRange"] = ["Uygunluk sorgusu pozitif ve en fazla 31 gün olmalıdır."];
        if (input.Page is < 1 or > 10000 || input.PageSize is < 1 or > 50)
            errors["page"] = ["Geçerli sayfa bilgileri girin."];
        return errors;
    }
    public static Dictionary<string, string[]> Calendar(CalendarInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.ToDateExclusive <= input.FromDate || input.ToDateExclusive.DayNumber - input.FromDate.DayNumber > 31)
            errors["dateRange"] = ["Tarih aralığı 1–31 gün olmalıdır."];
        if (input.Page is < 1 or > 10000 || input.PageSize is < 1 or > 50) errors["page"] = ["Geçerli sayfa bilgileri girin."];
        if (input.Status is { } status && !Enum.IsDefined(status)) errors["status"] = ["Geçerli randevu durumu seçin."];
        return errors;
    }
    public static void Snapshot(AppointmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!CustomerVehicleValidation.Categories.Contains(snapshot.Category)) SchedulingRules.Fail("invalidCategory", "Geçerli araç türü seçin.", 400);
        _ = SchedulingRules.Zone(snapshot.TimeZoneId);
    }
    /// <summary>Copy authoritative values from already loaded records, without inferring ownership or category from names/plates.</summary>
    public static AppointmentSnapshot CreateSnapshot(BusinessService service, ServiceOffering offering,
        LocalCustomer customer, LocalVehicle vehicle, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(service); ArgumentNullException.ThrowIfNull(offering);
        ArgumentNullException.ThrowIfNull(customer); ArgumentNullException.ThrowIfNull(vehicle);
        if (service.BusinessId == Guid.Empty || service.Id == Guid.Empty || customer.Id == Guid.Empty || vehicle.Id == Guid.Empty ||
            customer.BusinessId != service.BusinessId || vehicle.BusinessId != service.BusinessId ||
            vehicle.LocalCustomerId != customer.Id || offering.ServiceId != service.Id)
            SchedulingRules.Fail("resourceNotFound", "Müşteri, araç ve hizmet aynı işletmeye ait olmalıdır.", 404);
        if (!service.Available || vehicle.ArchivedAt != null)
            SchedulingRules.Fail("serviceUnavailable", "Arşivlenmiş araç veya kullanılamayan hizmet seçilemez.");
        if (offering.Category != vehicle.Category || !CustomerVehicleValidation.Categories.Contains(vehicle.Category))
            SchedulingRules.Fail("invalidCategory", "Hizmet seçilen araç türü için sunulmuyor.", 400);
        var snapshot = new AppointmentSnapshot(service.Name, customer.FullName, vehicle.Plate, vehicle.Category,
            offering.Price, offering.Currency, offering.ActiveMinutes, offering.WaitingMinutes, offering.Employees, timeZoneId);
        Snapshot(snapshot);
        return snapshot;
    }
    /// <summary>Hash the resolved instant and sorted employee IDs, not order-sensitive or culture-sensitive text.</summary>
    public static string RequestHash(BookingInput input, TimeZoneInfo zone)
    {
        if (Booking(input).Count != 0) SchedulingRules.Fail("validation", "İstek alanlarını kontrol edin.", 400);
        var resolvedStart = SchedulingRules.Resolve(input.StartLocal, input.UtcOffsetMinutes, zone);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new {
            input.LocalCustomerId, input.LocalVehicleId, input.ServiceId, input.ExpectedServiceVersion, input.ExpectedVehicleVersion,
            Start = resolvedStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Employees = input.EmployeeMembershipIds!.OrderBy(x => x).ToArray(), input.Source
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }
    public static AppointmentView View(Appointment appointment, uint version)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (version == 0) SchedulingRules.Fail("invalidVersion", "Kalıcı kaydın güncel sürümü gereklidir.", 400);
        return new(appointment.Id, appointment.BusinessId, version, appointment.Source, appointment.Status,
            appointment.LocalCustomerId, appointment.LocalVehicleId, appointment.ServiceId, appointment.WorkAreaId,
            appointment.ServiceName, appointment.CustomerName, appointment.VehiclePlate, appointment.Category,
            CatalogValidation.Money(appointment.Price), appointment.Currency, appointment.ActiveMinutes, appointment.WaitingMinutes,
            appointment.Employees, appointment.TimeZoneIdSnapshot, appointment.StartsAtUtc, appointment.ActiveEndsAtUtc,
            Array.AsReadOnly(appointment.Reservations.Select(x => new ReservationView(x.Id, x.WorkAreaId, x.EmployeeMembershipId,
                x.StartsAtUtc, x.EndsAtUtc, x.ReleasedAt)).ToArray()),
            Array.AsReadOnly(appointment.Events.Select(x => new EventView(x.Sequence, x.Kind, x.Status, x.PreviousStart,
                x.PreviousEnd, x.NewStart, x.NewEnd, x.CreatedAt)).ToArray()));
    }
}
