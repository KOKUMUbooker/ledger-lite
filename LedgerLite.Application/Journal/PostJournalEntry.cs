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
        // Validation is shared with the preview
        var plan = await JournalEntryPlanner.PlanAsync(_db, command, ct);

        if (plan.Existing is not null)
            return new(plan.Existing, WasDuplicate: true);

        var entry = plan.Entry!;

        // One SaveChanges = one transaction: all lines are saved or none are.
        _db.JournalEntries.Add(entry);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (plan.Key is not null)
        {
            // A concurrent request with the same key won the race (unique index on IdempotencyKey).
            _db.ChangeTracker.Clear();
            var existing = await JournalEntryPlanner.FindByKeyAsync(_db, plan.Key, ct);
            if (existing is not null) return new(existing, WasDuplicate: true);
            throw;
        }

        return new(entry, WasDuplicate: false);
    }
}