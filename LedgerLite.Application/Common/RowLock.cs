using LedgerLite.Data;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Common;

internal static class RowLock
{
    /// Holds an update lock on one account row until the surrounding transaction ends (SQL Server).
    /// Anyone else asking for the same lock waits, so check-then-post sequences can't interleave.
    public static Task AccountAsync(LedgerDbContext db, int accountId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM Accounts WITH (UPDLOCK, ROWLOCK) WHERE Id = {accountId}", ct);
}