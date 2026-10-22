using LedgerLite.Application.Journal;   
using LedgerLite.Data;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Savings;

public class DepositCash
{
    private readonly LedgerDbContext _db;
    private readonly PostJournalEntry _post;
    private readonly SystemAccountCodes _codes;

    public DepositCash(LedgerDbContext db, PostJournalEntry post, SystemAccountCodes codes)
    {
        _db = db;
        _post = post;
        _codes = codes;
    }

    public async Task<PostJournalEntryResult> ExecuteAsync(CashMovementCommand c, CancellationToken ct = default)
    {
        SavingsRules.ValidateAmount(c.Amount);
        var (savings, cash) = await SavingsRules.LoadAsync(_db, _codes, c.SavingsAccountId, ct);

        // Dr Cash (asset up), Cr Customer savings (we owe the customer more)
        return await _post.ExecuteAsync(new PostJournalEntryCommand(
            c.Date,
            string.IsNullOrWhiteSpace(c.Narration) ? $"Cash deposit · {savings.Name}" : c.Narration.Trim(),
            $"DEP-{Guid.NewGuid():N}"[..16],
            new[]
            {
                new PostLine(cash.Id, c.Amount, 0m),
                new PostLine(savings.Id, 0m, c.Amount)
            },
            c.IdempotencyKey,
            EntrySource.DepositCash), ct);
    }
}