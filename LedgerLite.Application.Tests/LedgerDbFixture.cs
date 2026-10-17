using LedgerLite.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Tests;

/// Creates a fresh, uniquely named SQL Server database by running the real migrations
/// (including the immutability triggers), and drops it when the tests finish.
public sealed class LedgerDbFixture : IAsyncLifetime
{
    private readonly string _dbName = $"LedgerLite_Test_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        // Set LEDGERLITE_TEST_CONNECTION to point at Docker or another server.
        var baseConnection =
            Environment.GetEnvironmentVariable("LEDGERLITE_TEST_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = _dbName
        }.ConnectionString;

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public LedgerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(_connectionString).Options);

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("Ledger")]
public class LedgerCollection : ICollectionFixture<LedgerDbFixture> { }