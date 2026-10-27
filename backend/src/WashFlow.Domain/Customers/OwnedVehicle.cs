using WashFlow.Domain.Common;
namespace WashFlow.Domain.Customers;

public sealed class OwnedVehicle : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CustomerProfileId { get; init; }
    public required string Plate { get; set; }
    public required string NormalizedPlate { get; set; }
    public required string Brand { get; set; }
    public required string Model { get; set; }
    public required string Category { get; set; }
    public int? ModelYear { get; set; }
    public string? Color { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
