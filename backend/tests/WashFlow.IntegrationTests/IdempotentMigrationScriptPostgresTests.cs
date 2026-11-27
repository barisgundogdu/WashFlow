using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class IdempotentMigrationScriptPostgresTests(PostgresModelFixture expectedFixture)
    : IClassFixture<PostgresModelFixture>
{
    private const string Baseline = "20261027125056_SchedulingPersistencePhase9B";

    [PostgresFact]
    public async Task GeneratedSchema7UpgradeExecutesThroughPsqlAndCanBeRepeatedWithoutDataOrCatalogDrift()
    {
        var original = Environment.GetEnvironmentVariable("WASHFLOW_TEST_POSTGRES")!;
        var schema = "washflow_script_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(original);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(original) { SearchPath = schema, Pooling = false };
            var options = new DbContextOptionsBuilder<WashFlowDbContext>().UseNpgsql(settings.ConnectionString,
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using var db = new WashFlowDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Baseline);
            var business = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Businesses\" (\"Id\",\"Name\",\"CreatedAt\",\"UpdatedAt\") VALUES ({business},{"Legacy script business"},{now},{now})");
            // Generate with the production EF/Npgsql generator, not a hand-edited SQL fixture.
            var script = migrator.GenerateScript(Baseline, options: MigrationsSqlGenerationOptions.Idempotent);
            var legacy = await LegacyRows(settings.ConnectionString);
            await Psql(settings, script);
            Assert.Equal(11, (await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(legacy, await LegacyRows(settings.ConnectionString));
            var actual = await Catalog(settings.ConnectionString, schema);
            await using var expectedDb = new WashFlowDbContext(expectedFixture.Options);
            var expectedSettings = new NpgsqlConnectionStringBuilder(expectedDb.Database.GetConnectionString());
            Assert.Equal(await Catalog(expectedSettings.ConnectionString, expectedSettings.SearchPath!), actual);
            foreach (var trigger in new[] { "scheduling_event_immutable", "scheduling_receipt_immutable",
                "scheduling_snapshot_immutable", "scheduling_reservation_history", "work_order_event_immutable",
                "work_order_identity_immutable", "appointment_stage_immutable", "finance_payments_immutable",
                "finance_manual_income_immutable", "finance_expenses_immutable" })
                Assert.Contains(actual, entry => entry.StartsWith("trigger|", StringComparison.Ordinal) && entry.Contains("|" + trigger + "|", StringComparison.Ordinal));
            // The same psql simple-query execution mode must also honor idempotency.
            await Psql(settings, script);
            Assert.Equal(11, (await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(legacy, await LegacyRows(settings.ConnectionString));
            Assert.Equal(actual, await Catalog(settings.ConnectionString, schema));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> LegacyRows(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT jsonb_agg(to_jsonb(b) - 'CancellationRescheduleCutoffMinutes' ORDER BY \"Id\")::text FROM \"Businesses\" b", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string[]> Catalog(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        const string sql = """
            SELECT 'constraint|' || r.relname || '|' || c.conname || '|' || pg_get_constraintdef(c.oid) || '|' || c.convalidated
            FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname=@schema
            UNION ALL SELECT 'index|' || r.relname || '|' || x.relname || '|' || pg_get_indexdef(i.indexrelid) || '|' || i.indisvalid || '|' || i.indisready
            FROM pg_index i JOIN pg_class r ON r.oid=i.indrelid JOIN pg_class x ON x.oid=i.indexrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname=@schema
            UNION ALL SELECT 'trigger|' || r.relname || '|' || t.tgname || '|' || pg_get_triggerdef(t.oid) || '|' || t.tgenabled::text
            FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname=@schema AND NOT t.tgisinternal
            UNION ALL SELECT 'function|' || p.proname || '|' || pg_get_functiondef(p.oid)
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema AND p.prokind='f'
            """;
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
        // The idempotent generator indents raw SQL inside DO blocks, including function bodies.
        // Ignore only line-edge whitespace, retaining internal tokens, literals and definitions.
        while (await reader.ReadAsync()) rows.Add(string.Join('\n', reader.GetString(0)
            .Replace(schema, "<schema>", StringComparison.Ordinal).Split('\n').Select(line => line.Trim())));
        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task Psql(NpgsqlConnectionStringBuilder settings, string script)
    {
        var container = Environment.GetEnvironmentVariable("WASHFLOW_TEST_PSQL_CONTAINER");
        var start = new ProcessStartInfo(container is null ? "psql" : "docker")
            { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (container is not null)
            foreach (var arg in new[] { "exec", "-i", "-e", "PGPASSWORD", "-e", "PGOPTIONS", container, "psql" }) start.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-X", "-qAt", "-v", "ON_ERROR_STOP=1", "-h", container is null ? settings.Host! : "127.0.0.1",
            "-p", container is null ? settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : "5432", "-U", settings.Username!, "-d", settings.Database! }) start.ArgumentList.Add(arg);
        start.Environment["PGPASSWORD"] = settings.Password;
        start.Environment["PGOPTIONS"] = "-c search_path=" + settings.SearchPath;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(script); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        await output;
        Assert.True(process.ExitCode == 0, "Generated migration script failed in psql: " + await error);
    }
}
