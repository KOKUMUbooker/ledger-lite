using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Reports;

public record TrialBalanceRow(
    int AccountId, string Code, string Name, AccountType Type,
    decimal TotalDebits, decimal TotalCredits, decimal Balance);

public record TrialBalance(
    DateOnly? AsOf,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal GrandTotalDebits,
    decimal GrandTotalCredits)
{
    /// The whole point of double-entry: this must always be true.
    public bool IsBalanced => GrandTotalDebits == GrandTotalCredits;
}

public class GetTrialBalance
{
    private readonly LedgerDbContext _db;

    public GetTrialBalance(LedgerDbContext db) => _db = db;

    public async Task<TrialBalance> ExecuteAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var lines =
            from l in _db.JournalLines.AsNoTracking()
            join e in _db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            select new { l.AccountId, e.Date, l.Debit, l.Credit };

        if (asOf is { } date)
            lines = lines.Where(x => x.Date <= date);

        // Sum in the database, not in memory
        var totals = await lines
            .GroupBy(x => x.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debits = g.Sum(x => x.Debit),
                Credits = g.Sum(x => x.Credit)
            })
            .ToDictionaryAsync(x => x.AccountId, ct);

        var accounts = await _db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);

        var rows = accounts.Select(a =>
        {
            totals.TryGetValue(a.Id, out var t);
            var debits = t?.Debits ?? 0m;
            var credits = t?.Credits ?? 0m;
            return new TrialBalanceRow(a.Id, a.Code, a.Name, a.Type, debits, credits,
                a.BalanceFrom(debits, credits));
        }).ToList();

        return new TrialBalance(asOf, rows, rows.Sum(r => r.TotalDebits), rows.Sum(r => r.TotalCredits));
    }
}