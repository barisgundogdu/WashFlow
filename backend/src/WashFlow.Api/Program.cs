using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WashFlow.Api.Errors;
using WashFlow.Application;
using WashFlow.Infrastructure;
using WashFlow.Infrastructure.Email;
using WashFlow.Infrastructure.Authentication;
using WashFlow.Api.Features.Authentication;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCoreAuthentication(builder.Configuration);
builder.Services.AddEmailVerification(builder.Configuration, builder.Environment);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("verification-resend", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblemDetails.Customize);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();
// Keep safe problem responses in Development too; never enable the developer exception page.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapDevelopmentEmailEndpoints();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});
app.Run();

static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    if (report.Status == HealthStatus.Healthy)
    {
        await context.Response.WriteAsJsonAsync(new { status = "healthy" }, context.RequestAborted);
        return;
    }

    var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problemDetails.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable }
    });
}

public partial class Program;
