using System.Security.Claims;
using WashFlow.Application.Scheduling;
namespace WashFlow.Api.Features.WorkOrders;
public static class StageSchedulingEndpoints
{
    public static void MapStageSchedulingEndpoints(this WebApplication app)
    {
        static SchedulingActor Actor(ClaimsPrincipal p)=>new(Guid.Parse(p.FindFirstValue("sub")!),Guid.Parse(p.FindFirstValue("sid")!));
        static async Task<IResult> Result(Func<Task<StageTemplatesView>> work)
        { try{return Results.Ok(await work());}catch(SchedulingFault e){return Results.Problem(statusCode:e.Status,extensions:new Dictionary<string,object?>{["code"]=e.Code});} }
        var group=app.MapGroup("/api/v1/businesses/{businessId:guid}/services/{serviceId:guid}/offerings/{category}/stages").RequireAuthorization().RequireRateLimiting("authentication");
        group.AddEndpointFilter(async(c,next)=>{c.HttpContext.Response.Headers.CacheControl="no-store";return await next(c);});
        group.MapGet("/",(Guid businessId,Guid serviceId,string category,ClaimsPrincipal p,IStageSchedulingService s,CancellationToken ct)=>Result(()=>s.Stages(Actor(p),businessId,serviceId,category,ct)));
        group.MapPut("/",(Guid businessId,Guid serviceId,string category,ClaimsPrincipal p,SaveStageTemplatesInput? input,IStageSchedulingService s,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(()=>s.SaveStages(Actor(p),businessId,serviceId,category,input,ct)));
    }
}
