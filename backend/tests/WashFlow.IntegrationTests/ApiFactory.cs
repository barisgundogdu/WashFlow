using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WashFlow.Application.Authentication;
using WashFlow.Infrastructure.Email;

namespace WashFlow.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Email:Provider", "Smtp");
        builder.UseSetting("Email:Smtp:Host", "unused.example.test");
        builder.UseSetting("Email:Smtp:From", "noreply@example.test");
        builder.UseSetting("Jwt:Issuer", "washflow-test");
        builder.UseSetting("Jwt:Audience", "washflow-test-client");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        // Host settings are available before the minimal API registers infrastructure.
        builder.UseSetting("ConnectionStrings:WashFlow",
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1;Pooling=false");
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, FailureFilter>();
            services.AddSingleton<DevelopmentEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<DevelopmentEmailSender>());
        });
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
