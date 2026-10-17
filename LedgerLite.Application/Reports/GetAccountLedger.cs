using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Reports;

public record LedgerLine(
    DateOnly Date, int EntryId, string Description, string Reference,
    decimal Debit, decimal Credit, decimal RunningBalance);

public record AccountLedger(
    int AccountId, string Code, string Name, AccountType Type,
    DateOnly? From, DateOnly? To,
    decimal OpeningBalance,
    IReadOnlyList<LedgerLine> Lines,
    decimal TotalDebits, decimal TotalCredits,
    decimal ClosingBalance);

public class GetAccountLedger
{
    private readonly LedgerDbContext _db;

    public GetAccountLedger(LedgerDbContext db) => _db = db;

    public async Task<AccountLedger> ExecuteAsync(
        int accountId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var account = await _db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new AccountNotFoundException(accountId);

        var allLines =
            from l in _db.JournalLines.AsNoTracking()
            join e in _db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where l.AccountId == accountId
            select new
            {
                LineId = l.Id,
                EntryId = e.Id,
                e.Date,
                e.Description,
                e.Reference,
                l.Debit,
                l.Credit
            };

        // Opening balance = everything before the 'from' date
        decimal opening = 0;
        if (from is { } fromDate)
        {
            var before = allLines.Where(x => x.Date < fromDate);
            var debitsBefore = await before.SumAsync(x => x.Debit, ct);
            var creditsBefore = await before.SumAsync(x => x.Credit, ct);
            opening = account.BalanceFrom(debitsBefore, creditsBefore);
        }

        var inRange = allLines;
        if (from is { } f) inRange = inRange.Where(x => x.Date >= f);
        if (to is { } t) inRange = inRange.Where(x => x.Date <= t);

        var rows = await inRange
            .OrderBy(x => x.Date).ThenBy(x => x.EntryId).ThenBy(x => x.LineId)
            .ToListAsync(ct);

        // The sign flip lives in Account.BalanceFrom, so it exists in exactly one place
        var running = opening;
        var lines = new List<LedgerLine>(rows.Count);
        foreach (var r in rows)
        {
            running += account.BalanceFrom(r.Debit, r.Credit);
            lines.Add(new LedgerLine(r.Date, r.EntryId, r.Description, r.Reference, r.Debit, r.Credit, running));
        }

        return new AccountLedger(
            account.Id, account.Code, account.Name, account.Type,
            from, to,
            opening,
            lines,
            TotalDebits: rows.Sum(r => r.Debit),
            TotalCredits: rows.Sum(r => r.Credit),
            ClosingBalance: running);
    }
}