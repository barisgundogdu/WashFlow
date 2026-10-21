using Microsoft.EntityFrameworkCore;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

public sealed class PostgresConnectivityTests
{
    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConfiguredPostgresAcceptsAReadOnlyQuery()
    {
        var options = new DbContextOptionsBuilder<WashFlowDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("WASHFLOW_TEST_POSTGRES"))
            .Options;
        await using var context = new WashFlowDbContext(options);
        Assert.True(await context.Database.CanConnectAsync());
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync());
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WASHFLOW_TEST_POSTGRES")))
            Skip = "WASHFLOW_TEST_POSTGRES is unset; a real PostgreSQL connection is required.";
    }
}
