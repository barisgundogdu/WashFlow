using System.Security.Claims;
using WashFlow.Application.Finance;
using WashFlow.Application.Scheduling;
namespace WashFlow.Api.Features.Finance;
public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this WebApplication app)
    {
        static SchedulingActor Actor(ClaimsPrincipal p)=>Guid.TryParse(p.FindFirstValue("sub"),out var u)&&u!=Guid.Empty&&Guid.TryParse(p.FindFirstValue("sid"),out var s)&&s!=Guid.Empty?new(u,s):throw new SchedulingFault(401,"unauthorized","Oturum geçersiz.");
        static async Task<IResult> Result(Func<Task<object>> action){try{return Results.Ok(await action());}catch(SchedulingFault e){return Results.Problem(statusCode:e.Status,extensions:new Dictionary<string,object?>{["code"]=e.Code});}}
        var root=app.MapGroup("/api/v1/businesses/{businessId:guid}/finance").RequireAuthorization().RequireRateLimiting("authentication");
        root.AddEndpointFilter(async(c,next)=>{c.HttpContext.Response.Headers.CacheControl="no-store";return await next(c);});
        root.MapGet("/settings",(Guid businessId,ClaimsPrincipal p,IFinanceService s,CancellationToken ct)=>Result(async()=>await s.Settings(Actor(p),businessId,ct)));
        root.MapPost("/methods",(Guid businessId,MethodInput? input,ClaimsPrincipal p,IFinanceService s,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.AddMethod(Actor(p),businessId,input,ct)));
        root.MapPost("/methods/{id:guid}/archive",(Guid businessId,Guid id,VersionInput? input,ClaimsPrincipal p,IFinanceService s,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.ArchiveMethod(Actor(p),businessId,id,input,ct)));
        root.MapGet("/receivables",(Guid businessId,ClaimsPrincipal p,IFinanceService s,string? status,int? page,CancellationToken ct)=>Result(async()=>await s.Receivables(Actor(p),businessId,status??"unpaid",page??1,ct)));
        root.MapGet("/report",(Guid businessId,ClaimsPrincipal p,IFinanceService s,DateOnly? fromDate,DateOnly? toDateExclusive,CancellationToken ct)=>Result(async()=>await s.Report(Actor(p),businessId,fromDate,toDateExclusive,ct)));
        root.MapGet("/entries/{kind}",(Guid businessId,string kind,ClaimsPrincipal p,IFinanceService s,int? page,CancellationToken ct)=>Result(async()=>await s.Entries(Actor(p),businessId,kind,page??1,ct)));
        root.MapPost("/entries/{kind}",(Guid businessId,string kind,FinanceEntryInput? input,ClaimsPrincipal p,IFinanceService s,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.Record(Actor(p),businessId,kind,input,ct)));
        root.MapPost("/entries/{kind}/{id:guid}/reverse",(Guid businessId,string kind,Guid id,ReverseEntryInput? input,ClaimsPrincipal p,IFinanceService s,CancellationToken ct)=>input==null?Task.FromResult<IResult>(Results.Problem(statusCode:400)):Result(async()=>await s.Reverse(Actor(p),businessId,kind,id,input,ct)));
    }
}
