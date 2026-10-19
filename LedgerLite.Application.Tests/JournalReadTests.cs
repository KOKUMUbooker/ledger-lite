using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class JournalReadTests : LedgerTestBase
{
    public JournalReadTests(LedgerDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task List_PagesNewestFirst_AndMarksReversedEntries()
    {
        var date = new DateOnly(2031, 1, 15);   // a date no other test uses, so counts are exact
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        var first = await PostAsync(cash.Id, deposits.Id, 100m, date);
        await PostAsync(cash.Id, deposits.Id, 200m, date);
        await PostAsync(cash.Id, deposits.Id, 300m, date);
        var reversal = await ReverseAsync(first.Entry.Id, date);   // 4 entries on that date

        await using var db = Fixture.CreateContext();
        var page1 = await new ListJournalEntries(db).ExecuteAsync(date, date, page: 1, pageSize: 3);
        var page2 = await new ListJournalEntries(db).ExecuteAsync(date, date, page: 2, pageSize: 3);

        Assert.Equal(4, page1.TotalCount);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(reversal.Id, page1.Items[0].Id);            // newest first

        var original = Assert.Single(page2.Items);               // the oldest entry lands on page 2
        Assert.Equal(first.Entry.Id, original.Id);
        Assert.Equal(reversal.Id, original.ReversedByEntryId);
        Assert.Equal(100m, original.Total);
        Assert.Equal(2, original.LineCount);
    }

    [Fact]
    public async Task Get_ReportsReversedByEntryId_OnlyAfterReversal()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var original = await PostAsync(cash.Id, deposits.Id, 100m);

        await using (var db = Fixture.CreateContext())
        {
            var before = await new GetJournalEntry(db).ExecuteAsync(original.Entry.Id);
            Assert.Null(before.ReversedByEntryId);
        }

        var reversal = await ReverseAsync(original.Entry.Id);

        await using var db2 = Fixture.CreateContext();
        var after = await new GetJournalEntry(db2).ExecuteAsync(original.Entry.Id);

        Assert.Equal(reversal.Id, after.ReversedByEntryId);
        Assert.Equal(2, after.Entry.Lines.Count);
    }

    [Fact]
    public async Task Get_UnknownEntry_Throws()
    {
        await using var db = Fixture.CreateContext();
        await Assert.ThrowsAsync<EntryNotFoundException>(() => new GetJournalEntry(db).ExecuteAsync(999_999));
    }
}