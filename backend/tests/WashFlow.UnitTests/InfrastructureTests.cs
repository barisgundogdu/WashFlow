using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WashFlow.Infrastructure;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.UnitTests;

public sealed class InfrastructureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionStringFailsWithoutDisclosingSecrets(string? connectionString)
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:WashFlow"] = connectionString;
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));
        Assert.Equal("ConnectionStrings__WashFlow yapılandırması gereklidir.", error.Message);
    }

    [Fact]
    public void ContextUsesPostgresAndIsScopedWithoutOpeningAConnection()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:WashFlow"] =
            "Host=localhost;Database=washflow_configuration_test;Username=test;Password=not-used";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<WashFlowDbContext>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<WashFlowDbContext>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<WashFlowDbContext>());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", first.Database.ProviderName);
        Assert.Empty(first.Model.GetEntityTypes());
    }
}
