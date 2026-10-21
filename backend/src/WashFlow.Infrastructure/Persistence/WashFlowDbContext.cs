using Microsoft.EntityFrameworkCore;

namespace WashFlow.Infrastructure.Persistence;

public sealed class WashFlowDbContext(DbContextOptions<WashFlowDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WashFlowDbContext).Assembly);
    }
}
