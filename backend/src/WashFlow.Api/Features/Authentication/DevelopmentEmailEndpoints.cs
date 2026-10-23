using System.Net;
using System.Text.Json;
using WashFlow.Application.Authentication;
using WashFlow.Infrastructure.Email;

namespace WashFlow.Api.Features.Authentication;

public static class DevelopmentEmailEndpoints
{
    public static void MapDevelopmentEmailEndpoints(this WebApplication app)
    {
        // Do not even register a route outside Development or when SMTP is selected.
        if (!app.Environment.IsDevelopment() ||
            app.Services.GetService<DevelopmentEmailSender>() is null) return;

        app.MapPost("/api/dev/email/verification/take", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";

            // Never trust Host or forwarded IPs. Reject proxy indicators even when
            // the proxy itself connects over loopback. X-Original-* also covers
            // headers consumed by ASP.NET Core ForwardedHeadersMiddleware.
            var peer = context.Connection.RemoteIpAddress;
            if (peer is null || !(peer.Equals(IPAddress.Loopback) || peer.Equals(IPAddress.IPv6Loopback)) ||
                context.Request.Headers.Keys.Any(IsProxyHeader))
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);

            if (!context.Request.HasJsonContentType())
                return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);

            ResendVerificationRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ResendVerificationRequest>(context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            }

            var errors = AuthValidation.Email(request?.Email);
            if (errors.Count != 0) return Results.ValidationProblem(errors);

            var mailbox = context.RequestServices.GetRequiredService<DevelopmentEmailSender>();
            // Atomic destructive read: only the latest message for this recipient.
            return mailbox.TryTake(request!.Email!.Trim(), out var email)
                ? Results.Json(email)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound);
        }).AllowAnonymous();
    }

    private static bool IsProxyHeader(string name) =>
        name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Via", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase);
}
