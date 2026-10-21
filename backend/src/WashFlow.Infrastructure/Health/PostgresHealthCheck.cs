using Microsoft.Extensions.Diagnostics.HealthChecks;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Health;

public sealed class PostgresHealthCheck(WashFlowDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connected = await dbContext.Database.CanConnectAsync(cancellationToken);
        return connected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Veritabanına erişilemiyor.");
    }
}
