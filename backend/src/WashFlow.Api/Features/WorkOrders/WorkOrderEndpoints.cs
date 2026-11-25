using System.Security.Claims;
using WashFlow.Application.Scheduling;
using WashFlow.Application.WorkOrders;
namespace WashFlow.Api.Features.WorkOrders;

public static class WorkOrderEndpoints
{
    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        static SchedulingActor Actor(ClaimsPrincipal p) => Guid.TryParse(p.FindFirstValue("sub"), out var user) && user != Guid.Empty && Guid.TryParse(p.FindFirstValue("sid"), out var session) && session != Guid.Empty
            ? new(user, session) : throw new SchedulingFault(401, "unauthorized", "Oturum geçersiz.");
        static async Task<IResult> Result(Func<Task<object>> run)
        {
            try { return Results.Ok(await run()); }
            catch (SchedulingFault e) { return Results.Problem(statusCode: e.Status, extensions: new Dictionary<string, object?> { ["code"] = e.Code }); }
        }
        var root = app.MapGroup("/api/v1/businesses/{businessId:guid}").RequireAuthorization().RequireRateLimiting("authentication");
        root.AddEndpointFilter(async (context, next) => { context.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(context); });
        foreach (var owner in new[] { true, false })
        {
            var isOwner = owner;
            var group = root.MapGroup(owner ? "/owner/work-orders" : "/employee/work-orders");
            group.MapGet("/", (Guid businessId, ClaimsPrincipal p, IWorkOrderService s, int? page, CancellationToken ct) =>
                Result(async () => await s.List(Actor(p), businessId, isOwner, page ?? 1, ct)));
            group.MapGet("/{id:guid}", (Guid businessId, Guid id, ClaimsPrincipal p, IWorkOrderService s, CancellationToken ct) =>
                Result(async () => await s.Detail(Actor(p), businessId, id, isOwner, ct)));
        }
        root.MapPost("/appointments/{appointmentId:guid}/work-order", (Guid businessId, Guid appointmentId, ClaimsPrincipal p, CreateWorkOrderInput? input, IWorkOrderService s, CancellationToken ct) =>
            input == null ? Task.FromResult<IResult>(Results.Problem(statusCode: 400)) : Result(async () => await s.Create(Actor(p), businessId, appointmentId, input, ct)));
        root.MapPost("/owner/work-orders/{id:guid}/cancel", (Guid businessId, Guid id, ClaimsPrincipal p, WorkActionInput? input, IWorkOrderService s, CancellationToken ct) =>
            input == null ? Task.FromResult<IResult>(Results.Problem(statusCode: 400)) : Result(async () => await s.Act(Actor(p), businessId, id, null, "cancel", input, ct)));
        root.MapPost("/employee/work-orders/{id:guid}/stages/{stage:guid}/{action}", (Guid businessId, Guid id, Guid stage, string action, ClaimsPrincipal p, WorkActionInput? input, IWorkOrderService s, CancellationToken ct) =>
            input == null || action == "cancel" ? Task.FromResult<IResult>(Results.Problem(statusCode: 400)) : Result(async () => await s.Act(Actor(p), businessId, id, stage, action, input, ct)));
    }
}
