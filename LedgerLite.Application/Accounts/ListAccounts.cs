using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Accounts;

public class ListAccounts
{
    private readonly LedgerDbContext _db;

    public ListAccounts(LedgerDbContext db) => _db = db;

    public async Task<IReadOnlyList<Account>> ExecuteAsync(AccountType? type = null, CancellationToken ct = default)
    {
        var query = _db.Accounts.AsNoTracking();

        if (type is { } t)
            query = query.Where(a => a.Type == t);

        return await query.OrderBy(a => a.Code).ToListAsync(ct);
    }
}