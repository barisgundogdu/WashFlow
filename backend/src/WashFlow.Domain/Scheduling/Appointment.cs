using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class Appointment : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid LocalCustomerId { get; init; }
    public Guid LocalVehicleId { get; init; }
    public Guid ServiceId { get; init; }
    public Guid WorkAreaId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public Guid ClientRequestId { get; init; }
    public required string RequestHash { get; init; }
    public required string Source { get; init; }
    public string Status { get; set; } = "Confirmed";
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset ActiveEndsAtUtc { get; set; }
    public required string TimeZoneIdSnapshot { get; init; }
    public required string ServiceName { get; init; }
    public required string CustomerName { get; init; }
    public required string VehiclePlate { get; init; }
    public required string Category { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "TRY";
    public int ActiveMinutes { get; init; }
    public int? WaitingMinutes { get; init; }
    public int Employees { get; init; }
}
