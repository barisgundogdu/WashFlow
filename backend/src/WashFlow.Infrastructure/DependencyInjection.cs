using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WashFlow.Infrastructure.Health;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("WashFlow");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__WashFlow yapılandırması gereklidir.");
        }

        services.AddDbContext<WashFlowDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
