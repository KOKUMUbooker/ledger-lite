using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

/// An entry plus the id of the entry that reversed it, if any.
public record JournalEntryView(JournalEntry Entry, int? ReversedByEntryId);

public class GetJournalEntry
{
    private readonly LedgerDbContext _db;

    public GetJournalEntry(LedgerDbContext db) => _db = db;

    public async Task<JournalEntryView> ExecuteAsync(int id, CancellationToken ct = default)
    {
        var entry = await _db.JournalEntries.AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new EntryNotFoundException(id);

        var reversedBy = await _db.JournalEntries.AsNoTracking()
            .Where(e => e.ReversalOfEntryId == id)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);

        return new JournalEntryView(entry, reversedBy);
    }
}