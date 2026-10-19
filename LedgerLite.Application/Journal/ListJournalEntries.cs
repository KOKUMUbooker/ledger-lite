using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

public record JournalEntrySummary(
    int Id, DateOnly Date, DateTime PostedAt, string Description, string Reference,
    EntrySource Source, int? ReversalOfEntryId, int? ReversedByEntryId, decimal Total, int LineCount);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public class ListJournalEntries
{
    private const int MaxPageSize = 100;
    private readonly LedgerDbContext _db;

    public ListJournalEntries(LedgerDbContext db) => _db = db;

    public async Task<PagedResult<JournalEntrySummary>> ExecuteAsync(
        DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _db.JournalEntries.AsNoTracking().AsQueryable();
        if (from is { } f) query = query.Where(e => e.Date >= f);
        if (to is { } t) query = query.Where(e => e.Date <= t);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Date,
                e.PostedAt,
                e.Description,
                e.Reference,
                e.Source,
                e.ReversalOfEntryId,
                ReversedBy = _db.JournalEntries
                    .Where(r => r.ReversalOfEntryId == e.Id)
                    .Select(r => (int?)r.Id)
                    .FirstOrDefault(),
                Total = e.Lines.Sum(l => l.Debit),
                LineCount = e.Lines.Count()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new JournalEntrySummary(
            r.Id, r.Date, r.PostedAt, r.Description, r.Reference, r.Source,
            r.ReversalOfEntryId, r.ReversedBy, r.Total, r.LineCount)).ToList();

        return new PagedResult<JournalEntrySummary>(items, page, pageSize, total);
    }
}