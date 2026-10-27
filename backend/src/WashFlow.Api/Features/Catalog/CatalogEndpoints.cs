using System.Security.Claims;
using WashFlow.Application.Catalog;
using WashFlow.Application.Customers;
namespace WashFlow.Api.Features.Catalog;

public sealed class ArchiveServiceInput { public uint? Version { get; set; } }
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var root = app.MapGroup("/api/v1/businesses/{businessId:guid}").RequireAuthorization().RequireRateLimiting("authentication");
        root.AddEndpointFilter(async (c, next) => { c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);
        root.MapGet("/services", async (Guid businessId, ClaimsPrincipal p, ICatalogStore store, string? q, string? status, int? page, int? pageSize, CancellationToken ct) => await List(store, Actor(p), businessId, q, status, page, pageSize, ct));
        root.MapGet("/published-services", async (Guid businessId, ICatalogStore store, string? q, int? page, int? pageSize, CancellationToken ct) => await List(store, null, businessId, q, "all", page, pageSize, ct));
        root.MapGet("/services/{serviceId:guid}", async (Guid businessId, Guid serviceId, ClaimsPrincipal p, ICatalogStore store, CancellationToken ct) => Result(await store.Read(Actor(p), businessId, serviceId, ct)));
        root.MapPost("/services", async (Guid businessId, ServiceInput? input, ClaimsPrincipal p, ICatalogStore store, CancellationToken ct) => await Save(store, Actor(p), businessId, null, input, ct));
        root.MapPut("/services/{serviceId:guid}", async (Guid businessId, Guid serviceId, ServiceInput? input, ClaimsPrincipal p, ICatalogStore store, CancellationToken ct) => await Save(store, Actor(p), businessId, serviceId, input, ct));
        root.MapPost("/services/{serviceId:guid}/archive", async (Guid businessId, Guid serviceId, ArchiveServiceInput? input, ClaimsPrincipal p, ICatalogStore store, CancellationToken ct) => input?.Version is null or 0 ? Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Güncel kayıt sürümü gereklidir."] }) : Result(await store.Archive(Actor(p), businessId, serviceId, input.Version.Value, ct), 204));
    }
    private static async Task<IResult> List(ICatalogStore store, Guid? actor, Guid business, string? q, string? status, int? page, int? size, CancellationToken ct) => !CustomerVehicleValidation.ValidPage(q, page ?? 1, size ?? 20) || status is not (null or "all" or "enabled" or "disabled") ? Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Geçerli arama, durum ve sayfa bilgileri girin."] }) : Result(await store.List(actor, business, q, status ?? "all", page ?? 1, size ?? 20, ct));
    private static async Task<IResult> Save(ICatalogStore store, Guid actor, Guid business, Guid? id, ServiceInput? input, CancellationToken ct)
    {
        input ??= new(); var errors = CatalogValidation.Validate(input, id != null);
        return errors.Count > 0 ? Results.ValidationProblem(errors) : Result(await store.Save(actor, business, id, input, ct), id == null ? 201 : 200);
    }
    private static IResult Result(ManagementResult r, int success = 200) => r.Status switch {
        ManagementStatus.Success => success == 204 ? Results.NoContent() : Results.Json(r.Value, statusCode: success),
        ManagementStatus.Forbidden => Results.Problem(statusCode: 403), ManagementStatus.NotFound => Results.Problem(statusCode: 404),
        _ => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = "recordConflict" })
    };
}
