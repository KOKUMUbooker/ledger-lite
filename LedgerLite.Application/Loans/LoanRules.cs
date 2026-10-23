using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Loans;

public record LoanSummary(
    Loan Loan, decimal PrincipalOutstanding, decimal InterestOutstanding)
{
    public decimal TotalOutstanding => PrincipalOutstanding + InterestOutstanding;
    public bool IsSettled => PrincipalOutstanding == 0m && InterestOutstanding == 0m;
}

internal static class LoanRules
{
    public static async Task<Loan> RequireAsync(LedgerDbContext db, int loanId, CancellationToken ct) =>
        await db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId, ct)
        ?? throw new LoanNotFoundException(loanId);

    /// Balance of one of the loan's (debit-normal asset) accounts, optionally as of a date.
    public static async Task<decimal> BalanceAsync(
        LedgerDbContext db, int accountId, DateOnly? asOf, CancellationToken ct)
    {
        var lines =
            from l in db.JournalLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId
            select new { e.Date, l.Debit, l.Credit };

        if (asOf is { } d) lines = lines.Where(x => x.Date <= d);

        return await lines.SumAsync(x => x.Debit, ct) - await lines.SumAsync(x => x.Credit, ct);
    }

    /// Balances for many loans in one grouped query (no N+1 for lists).
    public static async Task<List<LoanSummary>> SummariesAsync(
        LedgerDbContext db, IReadOnlyList<Loan> loans, CancellationToken ct)
    {
        var ids = loans.SelectMany(l => new[] { l.PrincipalAccountId, l.InterestAccountId }).ToList();

        var sums = await db.JournalLines.AsNoTracking()
            .Where(l => ids.Contains(l.AccountId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.Key, x => x.Net, ct);

        decimal Net(int id) => sums.TryGetValue(id, out var n) ? n : 0m;

        return loans.Select(l => new LoanSummary(l, Net(l.PrincipalAccountId), Net(l.InterestAccountId))).ToList();
    }
}