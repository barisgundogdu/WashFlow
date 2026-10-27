using WashFlow.Domain.Common;
namespace WashFlow.Domain.Catalog;

// Archive retains identity for future historical references. New operations must require Available.
public sealed class BusinessService : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public required string Name { get; set; }
    public required string SearchName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Available => Enabled && ArchivedAt == null;
}
public sealed class ServiceOffering
{
    public Guid ServiceId { get; init; }
    public required string Category { get; init; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public int ActiveMinutes { get; set; }
    public int? WaitingMinutes { get; set; }
    public int Employees { get; set; }
}
