using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WashFlow.Api.Errors;
using WashFlow.Application;
using WashFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblemDetails.Customize);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();
// Keep safe problem responses in Development too; never enable the developer exception page.
app.UseExceptionHandler();
app.UseStatusCodePages();
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
