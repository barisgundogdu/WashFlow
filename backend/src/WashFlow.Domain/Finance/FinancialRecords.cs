using WashFlow.Domain.Common;
namespace WashFlow.Domain.Finance;
public sealed class Payment : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public Guid RequestId { get; init; }
    public required string RequestHash { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TRY";
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ActorUserId { get; init; }
    public string? Description { get; init; }
    public string? Reference { get; init; }
    public Guid? CorrectsId { get; init; }
    public Guid? ReversesId { get; init; }
    public string? CorrectionReason { get; init; }
    public Guid WorkOrderId { get; init; }
    public Guid PaymentMethodId { get; init; }
    public required string PaymentMethodName { get; init; }
}
public sealed class ManualIncome : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public Guid RequestId { get; init; }
    public required string RequestHash { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TRY";
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ActorUserId { get; init; }
    public string? Description { get; init; }
    public string? Reference { get; init; }
    public Guid? CorrectsId { get; init; }
    public Guid? ReversesId { get; init; }
    public string? CorrectionReason { get; init; }
    public required string Category { get; init; }
    public Guid PaymentMethodId { get; init; }
    public required string PaymentMethodName { get; init; }
}
public sealed class Expense : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public Guid RequestId { get; init; }
    public required string RequestHash { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TRY";
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ActorUserId { get; init; }
    public string? Description { get; init; }
    public string? Reference { get; init; }
    public Guid? CorrectsId { get; init; }
    public Guid? ReversesId { get; init; }
    public string? CorrectionReason { get; init; }
    public required string Category { get; init; }
    public Guid? EmployeeMembershipId { get; init; }
}
public sealed class PaymentMethod : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public Guid ActorUserId { get; init; }
}
