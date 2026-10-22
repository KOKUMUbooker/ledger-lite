using LedgerLite.Application.Reports;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class LedgerPagingTests : LedgerTestBase
{
    public LedgerPagingTests(LedgerDbFixture fixture) : base(fixture) { }

    private async Task<AccountLedger> LedgerAsync(
        int accountId, DateOnly? from = null, int page = 1, int pageSize = 50)
    {
        await using var db = Fixture.CreateContext();
        return await new GetAccountLedger(db).ExecuteAsync(accountId, from, null, page, pageSize);
    }

    [Fact]
    public async Task RunningBalance_ContinuesAcrossPages()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        foreach (var amount in new[] { 100m, 200m, 300m, 400m, 500m })
            await PostAsync(cash.Id, deposits.Id, amount);

        var page1 = await LedgerAsync(cash.Id, page: 1, pageSize: 2);
        var page2 = await LedgerAsync(cash.Id, page: 2, pageSize: 2);
        var page3 = await LedgerAsync(cash.Id, page: 3, pageSize: 2);

        Assert.Equal(new[] { 100m, 300m }, page1.Lines.Select(l => l.RunningBalance));
        Assert.Equal(new[] { 600m, 1000m }, page2.Lines.Select(l => l.RunningBalance));
        Assert.Equal(new[] { 1500m }, page3.Lines.Select(l => l.RunningBalance));

        Assert.Equal(new[] { 0m, 300m, 1000m },
            new[] { page1.BroughtForward, page2.BroughtForward, page3.BroughtForward });
    }

    [Fact]
    public async Task Totals_CoverTheWholeRange_OnEveryPage()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        foreach (var amount in new[] { 100m, 200m, 300m, 400m, 500m })
            await PostAsync(cash.Id, deposits.Id, amount);

        foreach (var page in new[] { 1, 2, 3 })
        {
            var ledger = await LedgerAsync(cash.Id, page: page, pageSize: 2);

            Assert.Equal(5, ledger.TotalLines);
            Assert.Equal(1500m, ledger.TotalDebits);
            Assert.Equal(0m, ledger.TotalCredits);
            Assert.Equal(1500m, ledger.ClosingBalance);
        }
    }

    [Fact]
    public async Task WithFromDate_BroughtForwardStartsFromOpeningBalance()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await PostAsync(cash.Id, deposits.Id, 1000m, date: new DateOnly(2026, 9, 1));
        foreach (var amount in new[] { 100m, 200m, 300m })
            await PostAsync(cash.Id, deposits.Id, amount, date: new DateOnly(2026, 9, 20));

        var page1 = await LedgerAsync(cash.Id, from: new DateOnly(2026, 9, 15), page: 1, pageSize: 2);
        var page2 = await LedgerAsync(cash.Id, from: new DateOnly(2026, 9, 15), page: 2, pageSize: 2);

        Assert.Equal(1000m, page1.OpeningBalance);
        Assert.Equal(1000m, page1.BroughtForward);
        Assert.Equal(new[] { 1100m, 1300m }, page1.Lines.Select(l => l.RunningBalance));

        Assert.Equal(1300m, page2.BroughtForward);
        Assert.Equal(new[] { 1600m }, page2.Lines.Select(l => l.RunningBalance));
        Assert.Equal(1600m, page2.ClosingBalance);
    }

    [Fact]
    public async Task PageBeyondTheEnd_IsEmpty_ButTotalsStayCorrect()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await PostAsync(cash.Id, deposits.Id, 100m);
        await PostAsync(cash.Id, deposits.Id, 200m);

        var ledger = await LedgerAsync(cash.Id, page: 99, pageSize: 10);

        Assert.Empty(ledger.Lines);
        Assert.Equal(2, ledger.TotalLines);
        Assert.Equal(300m, ledger.ClosingBalance);
        Assert.Equal(ledger.ClosingBalance, ledger.BroughtForward);
    }

    [Fact]
    public async Task OutOfRangePaging_IsClamped()
    {
        var cash = await NewAccountAsync(AccountType.Asset);

        var ledger = await LedgerAsync(cash.Id, page: 0, pageSize: 100_000);

        Assert.Equal(1, ledger.Page);
        Assert.Equal(200, ledger.PageSize);
    }
}
