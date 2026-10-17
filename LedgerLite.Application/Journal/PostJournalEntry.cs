using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

public record PostLine(int AccountId, decimal Debit, decimal Credit);

public record PostJournalEntryCommand(
    DateOnly Date,
    string Description,
    string Reference,
    IReadOnlyList<PostLine> Lines,
    string? IdempotencyKey = null,
    EntrySource Source = EntrySource.Manual);

/// WasDuplicate is true when the idempotency key matched an entry that was already posted.
public record PostJournalEntryResult(JournalEntry Entry, bool WasDuplicate);

public class PostJournalEntry
{
    private readonly LedgerDbContext _db;

    public PostJournalEntry(LedgerDbContext db) => _db = db;

    public async Task<PostJournalEntryResult> ExecuteAsync(
        PostJournalEntryCommand command, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim();

        // 1. Retried request? Return what was already posted instead of posting again.
        if (key is not null)
        {
            var existing = await FindByKeyAsync(key, ct);
            if (existing is not null) return new(existing, WasDuplicate: true);
        }

        // 2. Every account must exist and be active.
        var accountIds = command.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var id in accountIds)
        {
            if (!accounts.TryGetValue(id, out var account))
                throw new AccountNotFoundException(id);
            if (!account.IsActive)
                throw new AccountInactiveException(account.Code);
        }

        // 3. The Domain validates balance, line rules and decimal places.
        var entry = JournalEntry.Create(
            command.Date,
            command.Description,
            command.Reference,
            command.Lines.Select(ToLine),
            command.Source,
            key);

        // 4. One SaveChanges = one transaction: all lines are saved or none are.
        _db.JournalEntries.Add(entry);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // A concurrent request with the same key won the race (unique index on IdempotencyKey).
            _db.ChangeTracker.Clear();
            var existing = await FindByKeyAsync(key, ct);
            if (existing is not null) return new(existing, WasDuplicate: true);
            throw;
        }

        return new(entry, WasDuplicate: false);
    }

    private Task<JournalEntry?> FindByKeyAsync(string key, CancellationToken ct) =>
        _db.JournalEntries.AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);

    private static JournalLine ToLine(PostLine line)
    {
        if (line.Debit != 0 && line.Credit != 0)
            throw new InvalidJournalEntryException("A line cannot have both a debit and a credit.");

        // Zero or negative amounts fall through to the Domain, which rejects them.
        return line.Credit != 0
            ? JournalLine.ForCredit(line.AccountId, line.Credit)
            : JournalLine.ForDebit(line.AccountId, line.Debit);
    }
}