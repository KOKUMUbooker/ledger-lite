using LedgerLite.Data;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Accounts;

internal static class AccountCodes
{
    /// Next free number for a zero-padded prefix like "SAV-000012". Codes are unique across ALL accounts.
    public static async Task<int> NextNumberAsync(LedgerDbContext db, string prefix, int digits, CancellationToken ct)
    {
        var last = await db.Accounts.AsNoTracking()
            .Where(a => a.Code.StartsWith(prefix) && a.Code.Length == prefix.Length + digits)
            .OrderByDescending(a => a.Code)
            .Select(a => a.Code)
            .FirstOrDefaultAsync(ct);

        return last is not null && int.TryParse(last.AsSpan(prefix.Length), out var n) ? n + 1 : 1;
    }

    public static string Format(string prefix, int digits, int number) =>
        prefix + number.ToString($"D{digits}");
}