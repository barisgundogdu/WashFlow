using System.Security.Claims;
using WashFlow.Application.CustomerBooking;
using WashFlow.Application.Scheduling;
namespace WashFlow.Api.Features.CustomerBooking;
public static class CustomerBookingEndpoints
{
    public static void MapCustomerBookingEndpoints(this WebApplication app)
    {
        static SchedulingActor Actor(ClaimsPrincipal p)=>Guid.TryParse(p.FindFirstValue("sub"),out var user)&&user!=Guid.Empty&&Guid.TryParse(p.FindFirstValue("sid"),out var session)&&session!=Guid.Empty?new(user,session):throw new SchedulingFault(401,"unauthorized","Oturum geçersiz.");
        static async Task<IResult> Result(Func<Task<object>> work)
        {try{return Results.Ok(await work());}catch(SchedulingFault e){return Results.Problem(statusCode:e.Status,extensions:new Dictionary<string,object?>{["code"]=e.Code});}}
        var root=app.MapGroup("/api/v1/customer").RequireAuthorization().RequireRateLimiting("authentication");
        root.AddEndpointFilter(async(c,next)=>{c.HttpContext.Response.Headers.CacheControl="no-store";return await next(c);});
        root.MapGet("/businesses",(ClaimsPrincipal p,ICustomerBookingService s,string? q,int? page,CancellationToken ct)=>Result(async()=>await s.Discover(Actor(p),q,page??1,ct)));
        root.MapGet("/businesses/{businessId:guid}",(Guid businessId,ClaimsPrincipal p,ICustomerBookingService s,int? page,CancellationToken ct)=>Result(async()=>await s.Business(Actor(p),businessId,page??1,ct)));
        root.MapGet("/businesses/{businessId:guid}/services/{serviceId:guid}/vehicles/{vehicleId:guid}/slots",(Guid businessId,Guid serviceId,Guid vehicleId,DateOnly date,ClaimsPrincipal p,ICustomerBookingService s,int? page,int? stepMinutes,Guid? appointmentId,CancellationToken ct)=>Result(async()=>await s.Slots(Actor(p),businessId,serviceId,vehicleId,date,page??1,stepMinutes??15,appointmentId,ct)));
        root.MapPost("/businesses/{businessId:guid}/appointments",async(Guid businessId,ClaimsPrincipal p,ICustomerBookingService s,CustomerBookingInput? input,CancellationToken ct)=>
        {
            if(input==null)return Results.Problem(statusCode:400);
            try{var r=await s.Book(Actor(p),businessId,input,ct);return r.Created?Results.Created($"/api/v1/customer/appointments/{r.Value.Id}",r.Value):Results.Ok(r.Value);}
            catch(SchedulingFault e){return Results.Problem(statusCode:e.Status,extensions:new Dictionary<string,object?>{["code"]=e.Code});}
        });
        root.MapGet("/appointments",(ClaimsPrincipal p,ICustomerBookingService s,string? status,int? page,CancellationToken ct)=>Result(async()=>await s.Mine(Actor(p),status??"all",page??1,ct)));
        root.MapGet("/appointments/{id:guid}",(Guid id,ClaimsPrincipal p,ICustomerBookingService s,CancellationToken ct)=>Result(async()=>await s.OwnDetail(Actor(p),id,ct)));
        root.MapPost("/appointments/{id:guid}/cancel",(Guid id,ClaimsPrincipal p,ICustomerBookingService s,CustomerChangeInput? input,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.CancelOwn(Actor(p),id,input,ct)));
        root.MapPost("/appointments/{id:guid}/reschedule",(Guid id,ClaimsPrincipal p,ICustomerBookingService s,CustomerRescheduleInput? input,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.RescheduleOwn(Actor(p),id,input,ct)));
    }
}
