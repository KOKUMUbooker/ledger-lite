using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Journal;

/// Everything that decides whether an entry may be posted, and nothing that saves it.
/// Posting and previewing both run exactly this, so they can never disagree about what is valid.
internal static class JournalEntryPlanner
{
    /// Either an existing entry (the idempotency key was already used) or a validated, unsaved new one.
    internal record Plan(
        string? Key, JournalEntry? Existing, JournalEntry? Entry, IReadOnlyDictionary<int, Account> Accounts);

    public static async Task<Plan> PlanAsync(
        LedgerDbContext db, PostJournalEntryCommand command, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim();

        // 1. Retried request? There is nothing new to validate.
        if (key is not null)
        {
            var existing = await FindByKeyAsync(db, key, ct);
            if (existing is not null)
                return new Plan(key, existing, null, new Dictionary<int, Account>());
        }

        // 2. Every account must exist and be active.
        var accountIds = command.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking()
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

        return new Plan(key, null, entry, accounts);
    }

    public static Task<JournalEntry?> FindByKeyAsync(LedgerDbContext db, string key, CancellationToken ct) =>
        db.JournalEntries.AsNoTracking()
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