using System.Security.Claims;
using WashFlow.Application.Authentication;
using WashFlow.Application.Invitations;
namespace WashFlow.Api.Features.Authentication;
public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireRateLimiting("authentication");
        group.AddEndpointFilter(async (context, next) => { context.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(context); });
        group.MapPost("/employee-invitations/register", async (InvitationRegistrationRequest? request, InvitationService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(request ?? new(), ct);
            return result.Errors is not null ? Results.ValidationProblem(result.Errors) : Results.Json(result.Value, statusCode: 202);
        }).AllowAnonymous();
        group.MapPost("/employee-invitations/accept", async (ClaimsPrincipal user, InvitationTokenRequest? request, InvitationService service, CancellationToken ct) =>
            Status(await service.AcceptAsync(Id(user), request?.Token, ct))).RequireAuthorization();
        var owner = group.MapGroup("/businesses/{businessId:guid}/employee-invitations").RequireAuthorization();
        owner.MapPost("/", async (Guid businessId, ClaimsPrincipal user, InvitationRequest? request, InvitationService service, CancellationToken ct) =>
        {
            var errors = AuthValidation.Email(request?.Email);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var result = await service.IssueAsync(Id(user), businessId, request!.Email, null, ct);
            return result.Status == InvitationStatus.Success ? Results.Json(result.Value, statusCode: 201) : Status(result.Status);
        });
        owner.MapGet("/", async (Guid businessId, ClaimsPrincipal user, InvitationService service, CancellationToken ct) =>
        {
            var rows = await service.ListAsync(Id(user), businessId, ct);
            return rows is null ? Results.Problem(statusCode: 403) : Results.Ok(rows);
        });
        owner.MapPost("/{invitationId:guid}/revoke", async (Guid businessId, Guid invitationId, ClaimsPrincipal user, InvitationService service, CancellationToken ct) =>
            Status(await service.RevokeAsync(Id(user), businessId, invitationId, ct)));
        owner.MapPost("/{invitationId:guid}/resend", async (Guid businessId, Guid invitationId, ClaimsPrincipal user, InvitationService service, CancellationToken ct) =>
        {
            var result = await service.IssueAsync(Id(user), businessId, null, invitationId, ct);
            return result.Status == InvitationStatus.Success ? Results.Ok(result.Value) : Status(result.Status);
        });
    }
    private static Guid Id(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
    private static IResult Status(InvitationStatus status) => status switch
    {
        InvitationStatus.Success => Results.NoContent(),
        InvitationStatus.Forbidden => Results.Problem(statusCode: 403),
        InvitationStatus.Conflict => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = "invitationConflict" }),
        _ => Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalidInvitation" })
    };
}
