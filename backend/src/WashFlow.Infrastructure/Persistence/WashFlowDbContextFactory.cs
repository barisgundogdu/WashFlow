using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WashFlow.Infrastructure.Persistence;

/// <summary>Allows offline model/migration commands without a fabricated connection string.</summary>
public sealed class WashFlowDbContextFactory : IDesignTimeDbContextFactory<WashFlowDbContext>
{
    public WashFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WashFlowDbContext>();
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__WashFlow");
        if (string.IsNullOrWhiteSpace(connection))
            options.UseNpgsql();
        else
            options.UseNpgsql(connection);
        return new WashFlowDbContext(options.Options);
    }
}
