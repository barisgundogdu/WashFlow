using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

/// <summary>Creates only a uniquely named test schema; never migrates the application's schema.</summary>
public sealed class PostgresModelFixture : IAsyncLifetime
{
    private readonly string schema = "washflow_test_" + Guid.NewGuid().ToString("N");
    private string? connectionString;
    private bool schemaCreated;
    public DbContextOptions<WashFlowDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("WASHFLOW_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        Options = new DbContextOptionsBuilder<WashFlowDbContext>()
            .UseNpgsql(builder.ConnectionString, options => options.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        schemaCreated = true;
        try
        {
            await using var context = new WashFlowDbContext(Options);
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!schemaCreated) return;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
        schemaCreated = false;
    }
}
