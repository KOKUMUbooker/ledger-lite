using LedgerLite.Application.Demo;
using LedgerLite.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Api.Maintenance;

/// Command-line tasks that run and exit instead of starting the web server:
///   dotnet run --project LedgerLite.Api -- reset-db --yes seed-demo
public static class MaintenanceCommands
{
    // Drops every foreign key and then every table (including the EF migrations history), so the migrations
    // can rebuild a clean schema. It works on hosts where you cannot drop or create the database itself.
    private const string DropEverything = """
        DECLARE @sql nvarchar(max) = N'';

        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.'
                     + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
        FROM sys.foreign_keys;
        EXEC sp_executesql @sql;

        SET @sql = N'';
        SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
        FROM sys.tables;
        EXEC sp_executesql @sql;
        """;

    /// Returns an exit code when a maintenance command ran, or null for a normal web start.
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        var reset = args.Contains("reset-db");
        var seed = args.Contains("seed-demo");
        if (!reset && !seed) return null;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        Console.WriteLine($"Target database: {Describe(db.Database.GetConnectionString())}");
        Console.WriteLine($"Environment:     {app.Environment.EnvironmentName}");

        try
        {
            if (reset)
            {
                if (!args.Contains("--yes"))
                {
                    Console.Error.WriteLine("reset-db DELETES every table and all data. Re-run with --yes to confirm.");
                    return 2;
                }
                if (app.Environment.IsProduction() && !args.Contains("--production"))
                {
                    Console.Error.WriteLine("This is the Production environment. Add --production to confirm.");
                    return 2;
                }

                await db.Database.ExecuteSqlRawAsync(DropEverything);
                await db.Database.MigrateAsync();   // recreates the schema and the starter chart of accounts
                Console.WriteLine("Database reset: tables dropped, migrations re-applied, starter accounts seeded.");
            }

            if (seed)
            {
                var summary = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();

                Console.WriteLine(
                    $"Demo data loaded: {summary.Customers} customers, {summary.Accounts} accounts, " +
                    $"{summary.Entries} entries, {summary.Loans} loans.");
                Console.WriteLine(summary.IsBalanced
                    ? $"Books balance: total debits = total credits = {summary.TotalDebits:N2}"
                    : "WARNING: the trial balance does not balance. Please report this.");

                if (!summary.IsBalanced) return 1;
            }

            return 0;
        }
        catch (DemoDataAlreadyPresentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // Server and database only: never print the password
    private static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "(no connection string configured)";
        var b = new SqlConnectionStringBuilder(connectionString);
        return $"{b.DataSource} / {b.InitialCatalog}";
    }
}