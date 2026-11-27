using WashFlow.Application.Businesses;
using WashFlow.Application.Scheduling;
namespace WashFlow.Application.CustomerBooking;

public sealed record DiscoveryBusiness(Guid Id,string Name,string? City,string? District,string? Address,string? Phone,string? Description,string? TimeZoneId);
public sealed record PublishedOffering(string Category,string Price,string Currency,int ActiveMinutes,int? WaitingMinutes,int Employees,bool SchedulingConfigured);
public sealed record PublishedService(Guid Id,uint Version,string Name,string? Description,IReadOnlyList<PublishedOffering> Offerings);
public sealed record CustomerBusinessDetail(DiscoveryBusiness Business,IReadOnlyList<OpeningDay>? Hours,IReadOnlyList<PublishedService> Services,int CancellationRescheduleCutoffMinutes,int Page,bool HasMore);
public sealed record CustomerSlot(DateTimeOffset StartsAtUtc,DateTimeOffset EndsAtUtc,string LocalLabel);
public sealed record CustomerSlotsView(Guid BusinessId,Guid ServiceId,Guid OwnedVehicleId,string VehiclePlate,string ServiceName,string Category,string Price,string Currency,int ActiveMinutes,uint ServiceVersion,uint VehicleVersion,IReadOnlyList<CustomerSlot> Items,int Page,bool HasMore,int StepMinutes);
public sealed record CustomerBookingInput(Guid ClientRequestId,Guid OwnedVehicleId,Guid ServiceId,uint ExpectedServiceVersion,uint ExpectedVehicleVersion,DateTimeOffset StartsAtUtc);
public sealed record CustomerChangeInput(Guid RequestId,uint Version);
public sealed record CustomerRescheduleInput(Guid RequestId,uint Version,DateTimeOffset StartsAtUtc);
public sealed record CustomerBookingView(Guid Id,Guid BusinessId,string BusinessName,Guid OwnedVehicleId,Guid ServiceId,uint Version,string VehiclePlate,string ServiceName,
    string Price,string Currency,int ActiveMinutes,string Status,string WorkStatus,DateTimeOffset StartsAtUtc,DateTimeOffset EndsAtUtc,string TimeZoneId,string LocalStart,
    int CancellationRescheduleCutoffMinutes,bool CanChange,DateTimeOffset ServerNowUtc);
public interface ICustomerBookingService
{
    Task<IReadOnlyList<DiscoveryBusiness>> Discover(SchedulingActor actor,string? query,int page=1,CancellationToken ct=default);
    Task<CustomerBusinessDetail> Business(SchedulingActor actor,Guid business,int page=1,CancellationToken ct=default);
    Task<CustomerSlotsView> Slots(SchedulingActor actor,Guid business,Guid service,Guid vehicle,DateOnly date,int page=1,int stepMinutes=15,Guid? appointmentId=null,CancellationToken ct=default);
    Task<SchedulingResult<CustomerBookingView>> Book(SchedulingActor actor,Guid business,CustomerBookingInput input,CancellationToken ct=default);
    Task<IReadOnlyList<CustomerBookingView>> Mine(SchedulingActor actor,string status="all",int page=1,CancellationToken ct=default);
    Task<CustomerBookingView> OwnDetail(SchedulingActor actor,Guid id,CancellationToken ct=default);
    Task<CustomerBookingView> CancelOwn(SchedulingActor actor,Guid id,CustomerChangeInput input,CancellationToken ct=default);
    Task<CustomerBookingView> RescheduleOwn(SchedulingActor actor,Guid id,CustomerRescheduleInput input,CancellationToken ct=default);
}
