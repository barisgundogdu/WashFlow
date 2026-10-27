using WashFlow.Domain.Common;
namespace WashFlow.Domain.Customers;

public sealed class LocalCustomer : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid BusinessId { get; init; }
    public required string FullName { get; set; }
    public required string NormalizedName { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
}
