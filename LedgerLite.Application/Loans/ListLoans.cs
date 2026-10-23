using LedgerLite.Data;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Loans;

public class ListLoans
{
    private readonly LedgerDbContext _db;
    public ListLoans(LedgerDbContext db) => _db = db;

    public async Task<IReadOnlyList<LoanSummary>> ExecuteAsync(CancellationToken ct = default)
    {
        var loans = await _db.Loans.AsNoTracking().OrderByDescending(l => l.Id).Take(200).ToListAsync(ct);
        return await LoanRules.SummariesAsync(_db, loans, ct);
    }
}