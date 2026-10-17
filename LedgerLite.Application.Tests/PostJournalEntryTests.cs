using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;
using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Tests;

public class PostJournalEntryTests : LedgerTestBase
{
    public PostJournalEntryTests(LedgerDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Post_BalancedEntry_PersistsEntryAndBothLines()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        var result = await PostAsync(cash.Id, deposits.Id, 5000m);

        await using var db = Fixture.CreateContext();
        var saved = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == result.Entry.Id);

        Assert.False(result.WasDuplicate);
        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(5000m, saved.Lines.Single(l => l.AccountId == cash.Id).Debit);
        Assert.Equal(5000m, saved.Lines.Single(l => l.AccountId == deposits.Id).Credit);
    }

    [Fact]
    public async Task Post_UnbalancedEntry_ThrowsAndSavesNothing()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var reference = $"UNBAL-{Guid.NewGuid():N}";

        await using (var db = Fixture.CreateContext())
        {
            await Assert.ThrowsAsync<UnbalancedEntryException>(() =>
                new PostJournalEntry(db).ExecuteAsync(new PostJournalEntryCommand(
                    Today, "Unbalanced", reference,
                    new[] { new PostLine(cash.Id, 5000m, 0), new PostLine(deposits.Id, 0, 4999m) })));
        }

        await using var check = Fixture.CreateContext();
        Assert.False(await check.JournalEntries.AnyAsync(e => e.Reference == reference));
        Assert.False(await check.JournalLines.AnyAsync(l => l.AccountId == cash.Id));
    }

    [Fact]
    public async Task Post_UnknownAccount_Throws()
    {
        var cash = await NewAccountAsync(AccountType.Asset);

        await Assert.ThrowsAsync<AccountNotFoundException>(() => PostAsync(cash.Id, 999_999, 100m));
    }

    [Fact]
    public async Task Post_InactiveAccount_Throws()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var closed = await NewAccountAsync(AccountType.Liability);

        await using (var db = Fixture.CreateContext())
        {
            var tracked = await db.Accounts.SingleAsync(a => a.Id == closed.Id);
            tracked.Deactivate();
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<AccountInactiveException>(() => PostAsync(cash.Id, closed.Id, 100m));
    }

    [Fact]
    public async Task Post_LineWithBothDebitAndCredit_Throws()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        await using var db = Fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidJournalEntryException>(() =>
            new PostJournalEntry(db).ExecuteAsync(new PostJournalEntryCommand(
                Today, "Both sides", "X",
                new[] { new PostLine(cash.Id, 100m, 100m), new PostLine(deposits.Id, 0, 100m) })));
    }

    [Fact]
    public async Task Post_SameIdempotencyKeyTwice_PostsOnlyOnce()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var key = $"accrual:{Guid.NewGuid():N}";

        var first = await PostAsync(cash.Id, deposits.Id, 250m, idempotencyKey: key);
        var second = await PostAsync(cash.Id, deposits.Id, 250m, idempotencyKey: key);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
        Assert.Equal(first.Entry.Id, second.Entry.Id);

        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.JournalLines.CountAsync(l => l.AccountId == cash.Id));
    }
}