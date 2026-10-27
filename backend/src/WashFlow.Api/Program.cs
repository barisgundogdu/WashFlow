using WashFlow.Api.Features.Scheduling;
using Microsoft.OpenApi;
using WashFlow.Api.Features.Catalog;
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
// Customer search can contain personal data. Never log raw request targets/query strings.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
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

builder.Services.AddOpenApi("v1", options =>
{
    // No OpenAPI setup existed before Phase 9C; publish the new scheduling surface explicitly.
    options.ShouldInclude = description => description.GroupName == "scheduling";
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "WashFlow Owner Scheduling API";
        document.Info.Version = "v1";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Geçerli oturum ve işletmede güncel Owner yetkisi gereklidir." };
        foreach (var path in document.Paths.Values)
            if (path.Operations != null)
                foreach (var operation in path.Operations.Values)
                {
                    operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                    // Nullable handler bodies allow our Turkish null-body validation, not optional requests.
                    if (operation.RequestBody is OpenApiRequestBody body) body.Required = true;
                }
        return Task.CompletedTask;
    });
});

var app = builder.Build();
app.MapCatalogEndpoints();
// Keep safe problem responses in Development too; never enable the developer exception page.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapSchedulingEndpoints();
app.MapOpenApi().RequireAuthorization();
app.MapAuthEndpoints();
app.MapInvitationEndpoints();
WashFlow.Api.Features.Customers.CustomerVehicleEndpoints.MapCustomerVehicleEndpoints(app);
WashFlow.Api.Features.Businesses.BusinessSettingsEndpoints.MapBusinessSettingsEndpoints(app);
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
