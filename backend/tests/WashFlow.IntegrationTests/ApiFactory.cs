using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace WashFlow.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        // Host settings are available before the minimal API registers infrastructure.
        builder.UseSetting("ConnectionStrings:WashFlow",
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1;Pooling=false");
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, FailureFilter>());
    }

    private sealed class FailureFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == "/__test/failure")
                    throw new InvalidOperationException("SENSITIVE_EXCEPTION_DETAIL");
                await nextMiddleware(context);
            });
        };
    }
}
