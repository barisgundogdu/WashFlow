using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WashFlow.Application.Authentication;

namespace WashFlow.Api.Features.Authentication;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").RequireRateLimiting("authentication");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });
        group.MapGet("/contexts", async (ClaimsPrincipal user,
            WashFlow.Application.Authorization.UserContextService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var userId) || userId == Guid.Empty)
                return Results.Unauthorized();
            return Results.Ok(await service.GetAsync(userId, ct));
        }).RequireAuthorization();
        group.MapPost("/customers/register", (RegistrationRequest? request, AuthService service, CancellationToken ct) =>
            Register(request ?? new(), false, service, ct)).AllowAnonymous();
        group.MapPost("/businesses/register", (RegistrationRequest? request, AuthService service, CancellationToken ct) =>
            Register(request ?? new(), true, service, ct)).AllowAnonymous();
        group.MapPost("/login", async (LoginRequest? request, AuthService service, CancellationToken ct) =>
            Tokens(await service.LoginAsync(request ?? new(), ct))).AllowAnonymous();
        group.MapPost("/refresh", async (RefreshRequest? request, AuthService service, CancellationToken ct) =>
            Tokens(await service.RefreshAsync(request ?? new(), ct))).AllowAnonymous();
        group.MapPost("/email/verify", async (VerifyEmailRequest? request, EmailVerificationService service, CancellationToken ct) =>
            await service.VerifyAsync(request ?? new(), ct)
                ? Results.Ok(new VerificationAcknowledgement("E-posta adresiniz doğrulandı. Giriş yapabilirsiniz."))
                : Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalidVerificationToken" }))
            .AllowAnonymous();
        group.MapPost("/email/resend", async (ResendVerificationRequest? request, EmailVerificationService service, CancellationToken ct) =>
        {
            var result = await service.ResendAsync(request ?? new(), ct);
            return result.Errors is not null ? Validation(result.Errors) : Results.Json(result.Value, statusCode: 202);
        }).AllowAnonymous().RequireRateLimiting("verification-resend");
        group.MapPost("/logout", async (ClaimsPrincipal user, AuthService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(Guid.Parse(user.FindFirstValue("sub")!), Guid.Parse(user.FindFirstValue("sid")!), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<IResult> Register(RegistrationRequest request, bool business, AuthService service, CancellationToken ct)
    {
        var result = await service.RegisterAsync(request, business, ct);
        return result.Errors is not null ? Validation(result.Errors) : Results.Json(result.Value, statusCode: 202);
    }
    private static IResult Tokens(AuthOutcome<TokenResponse> result) => result.Errors is not null
        ? Validation(result.Errors)
        : result.Succeeded ? Results.Ok(result.Value)
        : Results.Problem(statusCode: 401, extensions: new Dictionary<string, object?> { ["code"] = "invalidCredentials" });
    private static IResult Validation(Dictionary<string, string[]> errors) => Results.ValidationProblem(errors);
}
