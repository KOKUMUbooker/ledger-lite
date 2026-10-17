using LedgerLite.Application.Accounts;
using LedgerLite.Application.Journal;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

[Collection("Ledger")]
public abstract class LedgerTestBase
{
    protected static readonly DateOnly Today = new(2026, 9, 30);
    protected readonly LedgerDbFixture Fixture;

    protected LedgerTestBase(LedgerDbFixture fixture) => Fixture = fixture;

    /// The tests share one database, so each test creates its own accounts
    /// and never depends on what other tests have posted.
    protected async Task<Account> NewAccountAsync(AccountType type)
    {
        await using var db = Fixture.CreateContext();
        var code = $"T{Guid.NewGuid():N}"[..14];
        return await new CreateAccount(db).ExecuteAsync(new CreateAccountCommand(code, $"Test {code}", type));
    }

    protected async Task<PostJournalEntryResult> PostAsync(
        int debitAccountId, int creditAccountId, decimal amount,
        DateOnly? date = null, string? idempotencyKey = null)
    {
        await using var db = Fixture.CreateContext();
        return await new PostJournalEntry(db).ExecuteAsync(new PostJournalEntryCommand(
            date ?? Today,
            "Test posting",
            $"T-{Guid.NewGuid():N}",
            new[] { new PostLine(debitAccountId, amount, 0), new PostLine(creditAccountId, 0, amount) },
            idempotencyKey));
    }

    protected async Task<JournalEntry> ReverseAsync(int entryId, DateOnly? date = null)
    {
        await using var db = Fixture.CreateContext();
        return await new ReverseJournalEntry(db).ExecuteAsync(new ReverseJournalEntryCommand(entryId, date ?? Today));
    }
}