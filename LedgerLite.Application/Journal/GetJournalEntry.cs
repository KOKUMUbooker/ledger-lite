using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

public class GetJournalEntry
{
    private readonly LedgerDbContext _db;

    public GetJournalEntry(LedgerDbContext db) => _db = db;

    public async Task<JournalEntry> ExecuteAsync(int id, CancellationToken ct = default) =>
        await _db.JournalEntries.AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new EntryNotFoundException(id);
}