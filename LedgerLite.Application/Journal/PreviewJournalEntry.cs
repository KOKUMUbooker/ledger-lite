using System.Globalization;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

public record PreviewLine(
    int AccountId, string Code, string Name, AccountType Type,
    decimal Debit, decimal Credit, decimal BalanceBefore, decimal BalanceAfter);

public record JournalEntryPreview(
    DateOnly Date, string Description, string Reference,
    IReadOnlyList<PreviewLine> Lines,
    decimal TotalDebits, decimal TotalCredits,
    int? WouldReplayEntryId,
    IReadOnlyList<string> Warnings);

/// Runs the same checks as posting and reports what the entry would do. It never saves anything.
public class PreviewJournalEntry
{
    private readonly LedgerDbContext _db;

    public PreviewJournalEntry(LedgerDbContext db) => _db = db;

    public async Task<JournalEntryPreview> ExecuteAsync(
        PostJournalEntryCommand command, CancellationToken ct = default)
    {
        // Throws the same exceptions posting would, so the caller gets the same error messages
        var plan = await JournalEntryPlanner.PlanAsync(_db, command, ct);

        // The idempotency key was already used: posting would hand back that entry and add nothing new
        if (plan.Existing is { } existing)
        {
            return new JournalEntryPreview(
                existing.Date, existing.Description, existing.Reference,
                Array.Empty<PreviewLine>(),
                existing.Lines.Sum(l => l.Debit), existing.Lines.Sum(l => l.Credit),
                existing.Id, Array.Empty<string>());
        }

        var entry = plan.Entry!;
        var ids = plan.Accounts.Keys.ToList();

        // Current balances of the accounts involved, summed in the database in one query
        var sums = await _db.JournalLines.AsNoTracking()
            .Where(l => ids.Contains(l.AccountId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debits = g.Sum(x => x.Debit), Credits = g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.AccountId, ct);

        var running = new Dictionary<int, decimal>();
        foreach (var id in ids)
        {
            sums.TryGetValue(id, out var s);
            running[id] = plan.Accounts[id].BalanceFrom(s?.Debits ?? 0m, s?.Credits ?? 0m);
        }
        var opening = new Dictionary<int, decimal>(running);

        // Line by line, so an account that appears twice shows each step
        var lines = new List<PreviewLine>();
        foreach (var l in entry.Lines)
        {
            var account = plan.Accounts[l.AccountId];
            var before = running[l.AccountId];
            var after = before + account.BalanceFrom(l.Debit, l.Credit);
            running[l.AccountId] = after;

            lines.Add(new PreviewLine(
                account.Id, account.Code, account.Name, account.Type, l.Debit, l.Credit, before, after));
        }

        // Balances are signed towards the account's normal side, so below zero means unusual
        var warnings = ids
            .Where(id => running[id] < 0m && opening[id] >= 0m)
            .Select(id =>
            {
                var a = plan.Accounts[id];
                return $"{a.Code} · {a.Name} would go below zero " +
                       $"({running[id].ToString("N2", CultureInfo.InvariantCulture)}), " +
                       $"which is unusual for a {a.Type.ToString().ToLowerInvariant()} account.";
            })
            .ToList();

        return new JournalEntryPreview(
            entry.Date, entry.Description, entry.Reference, lines,
            entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit),
            null, warnings);
    }
}
