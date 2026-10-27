using System.Security.Claims;
using WashFlow.Application.Customers;
namespace WashFlow.Api.Features.Customers;

public sealed class ArchiveVehicleInput { public uint? Version { get; set; } }
public static class CustomerVehicleEndpoints
{
    public static void MapCustomerVehicleEndpoints(this WebApplication app)
    {
        var root = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("authentication");
        root.AddEndpointFilter(async (c, next) => { c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);
        var owner = root.MapGroup("/businesses/{businessId:guid}");
        owner.MapGet("/customers", async (Guid businessId, ClaimsPrincipal p, ICustomerVehicleStore s, string? q, int? page, int? pageSize, CancellationToken ct) =>
            !CustomerVehicleValidation.ValidPage(q, page ?? 1, pageSize ?? 20) ? InvalidPage() : Result(await s.Customers(Actor(p), businessId, q, page ?? 1, pageSize ?? 20, ct)));
        owner.MapGet("/customers/{customerId:guid}", async (Guid businessId, Guid customerId, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => Result(await s.Customer(Actor(p), businessId, customerId, ct)));
        owner.MapPost("/customers", async (Guid businessId, CustomerInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveCustomer(s, Actor(p), businessId, null, input, ct));
        owner.MapPut("/customers/{customerId:guid}", async (Guid businessId, Guid customerId, CustomerInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveCustomer(s, Actor(p), businessId, customerId, input, ct));
        owner.MapGet("/vehicles", async (Guid businessId, ClaimsPrincipal p, ICustomerVehicleStore s, string? q, int? page, int? pageSize, CancellationToken ct) => await Vehicles(s, Actor(p), businessId, null, q, page, pageSize, ct));
        var local = owner.MapGroup("/customers/{customerId:guid}/vehicles");
        local.MapGet("/", async (Guid businessId, Guid customerId, ClaimsPrincipal p, ICustomerVehicleStore s, string? q, int? page, int? pageSize, CancellationToken ct) => await Vehicles(s, Actor(p), businessId, customerId, q, page, pageSize, ct));
        local.MapPost("/", async (Guid businessId, Guid customerId, VehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveVehicle(s, Actor(p), businessId, customerId, null, input, ct));
        local.MapPut("/{vehicleId:guid}", async (Guid businessId, Guid customerId, Guid vehicleId, VehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveVehicle(s, Actor(p), businessId, customerId, vehicleId, input, ct));
        local.MapPost("/{vehicleId:guid}/archive", async (Guid businessId, Guid customerId, Guid vehicleId, ArchiveVehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await Archive(s, Actor(p), businessId, customerId, vehicleId, input, ct));
        var mine = root.MapGroup("/me/vehicles");
        mine.MapGet("/", async (ClaimsPrincipal p, ICustomerVehicleStore s, string? q, int? page, int? pageSize, CancellationToken ct) => await Vehicles(s, Actor(p), null, null, q, page, pageSize, ct));
        mine.MapPost("/", async (VehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveVehicle(s, Actor(p), null, null, null, input, ct));
        mine.MapPut("/{vehicleId:guid}", async (Guid vehicleId, VehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await SaveVehicle(s, Actor(p), null, null, vehicleId, input, ct));
        mine.MapPost("/{vehicleId:guid}/archive", async (Guid vehicleId, ArchiveVehicleInput? input, ClaimsPrincipal p, ICustomerVehicleStore s, CancellationToken ct) => await Archive(s, Actor(p), null, null, vehicleId, input, ct));
    }
    private static IResult InvalidPage() => Results.ValidationProblem(new Dictionary<string, string[]> { { "query", ["Arama en fazla 100 karakter; sayfa 1–10000, sayfa boyutu 1–50 olmalıdır."] } });
    private static async Task<IResult> Vehicles(ICustomerVehicleStore s, Guid actor, Guid? b, Guid? c, string? q, int? page, int? size, CancellationToken ct) => !CustomerVehicleValidation.ValidPage(q, page ?? 1, size ?? 20) ? InvalidPage() : Result(await s.Vehicles(actor, b, c, q, page ?? 1, size ?? 20, ct));
    private static async Task<IResult> SaveCustomer(ICustomerVehicleStore s, Guid actor, Guid b, Guid? c, CustomerInput? input, CancellationToken ct)
    {
        input ??= new(); var errors = CustomerVehicleValidation.Customer(input, c != null); return errors.Count > 0 ? Results.ValidationProblem(errors) : Result(await s.SaveCustomer(actor, b, c, input, ct), c == null ? 201 : 200);
    }
    private static async Task<IResult> SaveVehicle(ICustomerVehicleStore s, Guid actor, Guid? b, Guid? c, Guid? id, VehicleInput? input, CancellationToken ct)
    {
        input ??= new(); var errors = CustomerVehicleValidation.Vehicle(input, id != null); return errors.Count > 0 ? Results.ValidationProblem(errors) : Result(await s.SaveVehicle(actor, b, c, id, input, ct), id == null ? 201 : 200);
    }
    private static async Task<IResult> Archive(ICustomerVehicleStore s, Guid actor, Guid? b, Guid? c, Guid id, ArchiveVehicleInput? input, CancellationToken ct) => input?.Version is null or 0 ? Results.ValidationProblem(new Dictionary<string, string[]> { { "version", ["Güncel kayıt sürümü gereklidir."] } }) : Result(await s.ArchiveVehicle(actor, b, c, id, input.Version.Value, ct), 204);
    private static IResult Result(ManagementResult r, int success = 200) => r.Status switch
    {
        ManagementStatus.Success => success == 204 ? Results.NoContent() : Results.Json(r.Value, statusCode: success),
        ManagementStatus.Forbidden => Results.Problem(statusCode: 403),
        ManagementStatus.NotFound => Results.Problem(statusCode: 404),
        _ => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { { "code", "recordConflict" } })
    };
}
