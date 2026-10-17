using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Reports;
using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Tests;

public class ReverseJournalEntryTests : LedgerTestBase
{
    public ReverseJournalEntryTests(LedgerDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Reverse_CreatesLinkedEntry_AndBalancesNetToZero()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var original = await PostAsync(cash.Id, deposits.Id, 5000m);

        var reversal = await ReverseAsync(original.Entry.Id);

        Assert.Equal(original.Entry.Id, reversal.ReversalOfEntryId);
        Assert.Equal(EntrySource.Reversal, reversal.Source);

        await using var db = Fixture.CreateContext();
        var cashLedger = await new GetAccountLedger(db).ExecuteAsync(cash.Id);
        var depositLedger = await new GetAccountLedger(db).ExecuteAsync(deposits.Id);

        Assert.Equal(2, cashLedger.Lines.Count);   // the original is still there
        Assert.Equal(0m, cashLedger.ClosingBalance);
        Assert.Equal(0m, depositLedger.ClosingBalance);
    }

    [Fact]
    public async Task Reverse_SameEntryTwice_Throws()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var original = await PostAsync(cash.Id, deposits.Id, 100m);

        await ReverseAsync(original.Entry.Id);

        await Assert.ThrowsAsync<EntryAlreadyReversedException>(() => ReverseAsync(original.Entry.Id));
    }

    [Fact]
    public async Task Reverse_UnknownEntry_Throws()
    {
        await Assert.ThrowsAsync<EntryNotFoundException>(() => ReverseAsync(999_999));
    }

    [Fact]
    public async Task Reverse_AReversal_Throws()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var original = await PostAsync(cash.Id, deposits.Id, 100m);
        var reversal = await ReverseAsync(original.Entry.Id);

        await Assert.ThrowsAsync<InvalidJournalEntryException>(() => ReverseAsync(reversal.Id));
    }

    [Fact]
    public async Task Reverse_StillWorks_AfterAnAccountIsDeactivated()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var original = await PostAsync(cash.Id, deposits.Id, 100m);

        await using (var db = Fixture.CreateContext())
        {
            var tracked = await db.Accounts.SingleAsync(a => a.Id == deposits.Id);
            tracked.Deactivate();
            await db.SaveChangesAsync();
        }

        var reversal = await ReverseAsync(original.Entry.Id);

        Assert.Equal(original.Entry.Id, reversal.ReversalOfEntryId);
    }
}