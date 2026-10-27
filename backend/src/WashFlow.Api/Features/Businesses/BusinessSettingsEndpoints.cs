using System.Security.Claims;
using WashFlow.Application.Businesses;

namespace WashFlow.Api.Features.Businesses;

public static class BusinessSettingsEndpoints
{
    public static void MapBusinessSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/businesses/{businessId:guid}/settings").RequireAuthorization().RequireRateLimiting("authentication");
        group.AddEndpointFilter(async (c, next) => { c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        group.MapGet("/", async (Guid businessId, ClaimsPrincipal actor, IBusinessSettingsStore store, CancellationToken ct) =>
            Result(await store.ReadAsync(Guid.Parse(actor.FindFirstValue("sub")!), businessId, ct)));
        group.MapPost("/", async (Guid businessId, ClaimsPrincipal actor, BusinessSettingsInput? request, IBusinessSettingsStore store, CancellationToken ct) =>
        {
            request ??= new();
            var errors = BusinessSettingsValidation.Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            return Result(await store.UpdateAsync(Guid.Parse(actor.FindFirstValue("sub")!), businessId, request, ct));
        });
    }
    private static IResult Result(SettingsResult r) => r.Status switch
    {
        SettingsStatus.Success => Results.Ok(r.Value),
        SettingsStatus.Conflict => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = "settingsConflict" }),
        _ => Results.Problem(statusCode: 403)
    };
}
