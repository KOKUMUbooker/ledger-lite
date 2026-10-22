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
    decimal OpeningBalance,                       // balance before the range starts
    int Page, int PageSize, int TotalLines,       // paging, TotalLines covers the whole range
    decimal BroughtForward,                       // running balance at the start of THIS page
    IReadOnlyList<LedgerLine> Lines,              // this page only
    decimal TotalDebits, decimal TotalCredits,    // whole range
    decimal ClosingBalance);                      // whole range

public class GetAccountLedger
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly LedgerDbContext _db;

    public GetAccountLedger(LedgerDbContext db) => _db = db;

    public async Task<AccountLedger> ExecuteAsync(
        int accountId, DateOnly? from = null, DateOnly? to = null,
        int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

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

        // Whole-range figures, computed in the database, so they don't depend on which page is shown
        var totalDebits = await inRange.SumAsync(x => x.Debit, ct);
        var totalCredits = await inRange.SumAsync(x => x.Credit, ct);
        var totalLines = await inRange.CountAsync(ct);

        // The sign flip lives in Account.BalanceFrom, so it exists in exactly one place
        var closing = opening + account.BalanceFrom(totalDebits, totalCredits);

        // A stable order is what makes paging correct: date, then entry, then line
        var ordered = inRange.OrderBy(x => x.Date).ThenBy(x => x.EntryId).ThenBy(x => x.LineId);
        var skip = (page - 1) * pageSize;

        // The running balance at the top of this page = opening + everything on the earlier pages
        var broughtForward = opening;
        if (skip > 0)
        {
            var earlier = ordered.Take(skip);
            broughtForward += account.BalanceFrom(
                await earlier.SumAsync(x => x.Debit, ct),
                await earlier.SumAsync(x => x.Credit, ct));
        }

        var rows = await ordered.Skip(skip).Take(pageSize).ToListAsync(ct);

        var running = broughtForward;
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
            page, pageSize, totalLines,
            broughtForward,
            lines,
            totalDebits, totalCredits,
            closing);
    }
}