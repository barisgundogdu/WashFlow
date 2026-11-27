using WashFlow.Application.Scheduling;
namespace WashFlow.Application.Finance;
public sealed record PaymentMethodView(Guid Id,string Name,bool Archived,uint Version);
public sealed record FinanceEmployee(Guid Id,string Name);
public sealed record FinanceSettings(IReadOnlyList<PaymentMethodView> Methods,IReadOnlyList<FinanceEmployee> Employees);
public sealed record MethodInput(string Name);
public sealed record FinanceEntryInput(Guid RequestId,string Amount,DateTimeOffset OccurredAtUtc,string? Category=null,string? Description=null,string? Reference=null,Guid? WorkOrderId=null,Guid? PaymentMethodId=null,Guid? EmployeeMembershipId=null,Guid? CorrectsId=null,string? CorrectionReason=null);
public sealed record ReverseEntryInput(Guid RequestId,string Reason);
public sealed record FinanceEntryView(Guid Id,Guid BusinessId,string Kind,string Amount,string Currency,DateTimeOffset OccurredAtUtc,string? Category,string? Description,string? Reference,Guid? WorkOrderId,Guid? PaymentMethodId,string? PaymentMethodName,Guid? EmployeeMembershipId,Guid? ReversesId,Guid? CorrectsId,string? CorrectionReason,Guid ActorUserId,DateTimeOffset CreatedAt);
public sealed record ReceivableView(Guid WorkOrderId,string VehiclePlate,string ServiceName,DateTimeOffset CompletedAtUtc,string Price,string Collected,string Remaining,string Status);
public sealed record FinanceBucket(string Label,string Amount);
public sealed record FinanceTrend(string Date,string EarnedRevenue,string CollectedPayments,string ManualIncome,string Expenses);
public sealed record FinanceReport(DateOnly FromDate,DateOnly ToDateExclusive,string BusinessDate,string TimeZoneId,string Currency,string EarnedRevenue,string CollectedPayments,string ManualIncome,string Expenses,string ServiceOperatingResult,string CashOperatingBalance,string UnpaidBalance,IReadOnlyList<FinanceBucket> ServiceRevenue,IReadOnlyList<FinanceBucket> ExpenseCategories,IReadOnlyList<FinanceBucket> PaymentMethods,IReadOnlyList<FinanceTrend> Trends);
public interface IFinanceService
{
    Task<FinanceSettings> Settings(SchedulingActor actor,Guid business,CancellationToken ct=default);
    Task<PaymentMethodView> AddMethod(SchedulingActor actor,Guid business,MethodInput input,CancellationToken ct=default);
    Task<PaymentMethodView> ArchiveMethod(SchedulingActor actor,Guid business,Guid id,VersionInput input,CancellationToken ct=default);
    Task<IReadOnlyList<ReceivableView>> Receivables(SchedulingActor actor,Guid business,string status="unpaid",int page=1,CancellationToken ct=default);
    Task<IReadOnlyList<FinanceEntryView>> Entries(SchedulingActor actor,Guid business,string kind,int page=1,CancellationToken ct=default);
    Task<FinanceEntryView> Record(SchedulingActor actor,Guid business,string kind,FinanceEntryInput input,CancellationToken ct=default);
    Task<FinanceEntryView> Reverse(SchedulingActor actor,Guid business,string kind,Guid id,ReverseEntryInput input,CancellationToken ct=default);
    Task<FinanceReport> Report(SchedulingActor actor,Guid business,DateOnly? from=null,DateOnly? to=null,CancellationToken ct=default);
}
