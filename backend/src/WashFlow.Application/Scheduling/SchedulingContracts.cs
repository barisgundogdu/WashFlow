using System.Text.Json.Serialization;
using WashFlow.Domain.Scheduling;

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
// Persistence operations are deliberately not defined by a string/object dispatcher.
// Phase 9B will introduce a typed transactional boundary; no store implementation exists in 9A.
public sealed record SchedulingResult<T>(T Value, bool Created = false);
public sealed record AvailabilityCheckInput(Guid ServiceId, string Category, string? StartLocal,
    int? UtcOffsetMinutes, IReadOnlyList<Guid>? EmployeeMembershipIds);
public sealed record CalendarInput(DateOnly FromDate, DateOnly ToDateExclusive, int Page = 1, int PageSize = 20,
    AppointmentStatus? Status = null);
public sealed record WorkAreaView(Guid Id, Guid BusinessId, string Name, int Capacity, DateTimeOffset? ArchivedAt, uint Version);
public sealed record AvailabilityView(Guid Id, Guid BusinessId, Guid MembershipId, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, DateTimeOffset? ArchivedAt, uint Version);
public sealed record RequirementView(Guid Id, Guid BusinessId, Guid ServiceId, string Category, Guid WorkAreaId,
    bool SingleStageConfirmed, uint Version);
public sealed record EmployeeView(Guid MembershipId, Guid BusinessId, string Name);
public sealed record ReservationView(Guid Id, Guid? WorkAreaId, Guid? EmployeeMembershipId,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, DateTimeOffset? ReleasedAt);
public sealed record EventView(int Sequence,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppointmentEventKind>))] AppointmentEventKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppointmentStatus>))] AppointmentStatus Status,
    DateTimeOffset? PreviousStart, DateTimeOffset? PreviousEnd, DateTimeOffset NewStart, DateTimeOffset NewEnd,
    DateTimeOffset OccurredAt);
public sealed record AppointmentView(Guid Id, Guid BusinessId, uint Version,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BookingSource>))] BookingSource Source,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppointmentStatus>))] AppointmentStatus Status,
    Guid LocalCustomerId, Guid LocalVehicleId, Guid ServiceId, Guid WorkAreaId,
    string ServiceName, string CustomerName, string VehiclePlate, string Category, string Price, string Currency,
    int ActiveMinutes, int? WaitingMinutes, int Employees, string TimeZoneId,
    DateTimeOffset StartsAtUtc, DateTimeOffset ActiveEndsAtUtc,
    IReadOnlyList<ReservationView> Reservations, IReadOnlyList<EventView> Events);
public sealed record AvailabilityCheckView(bool Available, DateTimeOffset StartsAtUtc, DateTimeOffset ActiveEndsAtUtc);
