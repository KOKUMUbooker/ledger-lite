using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   // adjust to the namespace of PostJournalEntry
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Savings;

public class WithdrawCash
{
    private readonly LedgerDbContext _db;
    private readonly PostJournalEntry _post;
    private readonly SystemAccountCodes _codes;

    public WithdrawCash(LedgerDbContext db, PostJournalEntry post, SystemAccountCodes codes)
    {
        _db = db;
        _post = post;
        _codes = codes;
    }

    public async Task<PostJournalEntryResult> ExecuteAsync(CashMovementCommand c, CancellationToken ct = default)
    {
        SavingsRules.ValidateAmount(c.Amount);
        var (savings, cash) = await SavingsRules.LoadAsync(_db, _codes, c.SavingsAccountId, ct);

        // A replayed request must return the original entry, even though the balance has since dropped.
        // So the funds check only applies to a request we haven't seen before.
        var isReplay = c.IdempotencyKey is not null &&
                       await _db.JournalEntries.AnyAsync(e => e.IdempotencyKey == c.IdempotencyKey, ct);

        if (!isReplay)
        {
            var balance = await SavingsRules.BalanceAsync(_db, savings, ct);
            if (balance < c.Amount)
                throw new InsufficientFundsException(savings.Code, balance, c.Amount);
        }

        // Dr Customer savings (we owe less), Cr Cash (asset down)
        return await _post.ExecuteAsync(new PostJournalEntryCommand(
            c.Date,
            string.IsNullOrWhiteSpace(c.Narration) ? $"Cash withdrawal · {savings.Name}" : c.Narration.Trim(),
            $"WDR-{Guid.NewGuid():N}"[..16],
            new[]
            {
                new PostLine(savings.Id, c.Amount, 0m),
                new PostLine(cash.Id, 0m, c.Amount)
            },
            c.IdempotencyKey,
            EntrySource.WithdrawCash), ct);
    }
}