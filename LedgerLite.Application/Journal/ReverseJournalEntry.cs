using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

public record ReverseJournalEntryCommand(int EntryId, DateOnly Date, string? Description = null);

public class ReverseJournalEntry
{
    private readonly LedgerDbContext _db;

    public ReverseJournalEntry(LedgerDbContext db) => _db = db;

    public async Task<JournalEntry> ExecuteAsync(
        ReverseJournalEntryCommand command, CancellationToken ct = default)
    {
        var original = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == command.EntryId, ct)
            ?? throw new EntryNotFoundException(command.EntryId);

        if (await IsReversedAsync(original.Id, ct))
            throw new EntryAlreadyReversedException(original.Id);

        // The Domain refuses to reverse a reversal, and builds the flipped lines.
        // Note: we deliberately do NOT check whether accounts are still active.
        // Correcting history must work even after an account has been closed.
        var reversal = original.CreateReversal(command.Date, command.Description);

        _db.JournalEntries.Add(reversal);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two people reversing the same entry at once: the unique index on ReversalOfEntryId catches it.
            if (await IsReversedAsync(original.Id, ct))
                throw new EntryAlreadyReversedException(original.Id);
            throw;
        }

        return reversal;
    }

    private Task<bool> IsReversedAsync(int entryId, CancellationToken ct) =>
        _db.JournalEntries.AnyAsync(e => e.ReversalOfEntryId == entryId, ct);
}