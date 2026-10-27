namespace WashFlow.Infrastructure.Scheduling;

// Stored in the same transaction as the aggregate, preserving the original response across
// later reschedules/cancellation. The appointment retains the canonical hash and timezone.
public sealed class SchedulingCreateReceipt
{
    public Guid BusinessId { get; init; }
    public Guid ClientRequestId { get; init; }
    public Guid AppointmentId { get; init; }
    public required string ResultJson { get; init; }
}
