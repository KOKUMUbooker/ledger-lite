using LedgerLite.Application.Accounts;
using LedgerLite.Application.Reports;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class ReportTests : LedgerTestBase
{
    public ReportTests(LedgerDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Ledger_RunningBalance_FlipsSignByAccountType()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        await PostAsync(cash.Id, deposits.Id, 5000m);   // deposit
        await PostAsync(deposits.Id, cash.Id, 2000m);   // withdrawal

        await using var db = Fixture.CreateContext();
        var cashLedger = await new GetAccountLedger(db).ExecuteAsync(cash.Id);
        var depositLedger = await new GetAccountLedger(db).ExecuteAsync(deposits.Id);

        // Asset: debit increases, credit decreases
        Assert.Equal(new[] { 5000m, 3000m }, cashLedger.Lines.Select(l => l.RunningBalance));
        Assert.Equal(3000m, cashLedger.ClosingBalance);

        // Liability: credit increases, debit decreases
        Assert.Equal(new[] { 5000m, 3000m }, depositLedger.Lines.Select(l => l.RunningBalance));
        Assert.Equal(3000m, depositLedger.ClosingBalance);
    }

    [Fact]
    public async Task Ledger_WithFromDate_StartsFromOpeningBalance()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        await PostAsync(cash.Id, deposits.Id, 5000m, date: new DateOnly(2026, 9, 1));
        await PostAsync(cash.Id, deposits.Id, 1000m, date: new DateOnly(2026, 9, 20));

        await using var db = Fixture.CreateContext();
        var ledger = await new GetAccountLedger(db).ExecuteAsync(cash.Id, from: new DateOnly(2026, 9, 15));

        Assert.Equal(5000m, ledger.OpeningBalance);
        Assert.Single(ledger.Lines);
        Assert.Equal(6000m, ledger.Lines[0].RunningBalance);
        Assert.Equal(6000m, ledger.ClosingBalance);
    }

    [Fact]
    public async Task TrialBalance_StaysBalanced_AfterManyPostingsAndReversals()
    {
        var accounts = new[]
        {
            await NewAccountAsync(AccountType.Asset),
            await NewAccountAsync(AccountType.Liability),
            await NewAccountAsync(AccountType.Income),
            await NewAccountAsync(AccountType.Expense)
        };

        var random = new Random(42);   // fixed seed, so failures are reproducible
        var posted = new List<int>();

        for (var i = 0; i < 30; i++)
        {
            var debit = accounts[random.Next(accounts.Length)];
            var credit = accounts.Where(a => a.Id != debit.Id).ElementAt(random.Next(accounts.Length - 1));
            var amount = random.Next(1, 100_000) / 100m;   // up to 2 decimals

            posted.Add((await PostAsync(debit.Id, credit.Id, amount)).Entry.Id);
        }

        foreach (var id in posted.Where((_, i) => i % 5 == 0))
            await ReverseAsync(id);

        await using var db = Fixture.CreateContext();
        var trial = await new GetTrialBalance(db).ExecuteAsync();

        Assert.True(trial.IsBalanced);
        Assert.Equal(trial.GrandTotalDebits, trial.GrandTotalCredits);
    }

    [Fact]
    public async Task TrialBalance_AsOfDate_ExcludesLaterEntries()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        await PostAsync(cash.Id, deposits.Id, 5000m, date: new DateOnly(2026, 9, 1));
        await PostAsync(cash.Id, deposits.Id, 1000m, date: new DateOnly(2026, 9, 20));

        await using var db = Fixture.CreateContext();
        var early = await new GetTrialBalance(db).ExecuteAsync(asOf: new DateOnly(2026, 9, 10));
        var late = await new GetTrialBalance(db).ExecuteAsync(asOf: new DateOnly(2026, 9, 30));

        Assert.Equal(5000m, early.Rows.Single(r => r.AccountId == cash.Id).Balance);
        Assert.Equal(6000m, late.Rows.Single(r => r.AccountId == cash.Id).Balance);
        Assert.True(early.IsBalanced);
        Assert.True(late.IsBalanced);
    }

    [Fact]
    public async Task TrialBalance_ParentRollsUpItsChildren_WithoutChangingTheTotals()
    {
        var parent = await NewAccountAsync(AccountType.Liability);
        var cash = await NewAccountAsync(AccountType.Asset);
        Account child;
        await using (var db = Fixture.CreateContext())
        {
            child = await new CreateAccount(db).ExecuteAsync(new CreateAccountCommand(
                $"C{Guid.NewGuid():N}"[..14], "Child", AccountType.Liability, parent.Id));
        }
        await PostAsync(cash.Id, child.Id, 300m);

        await using var read = Fixture.CreateContext();
        var tb = await new GetTrialBalance(read).ExecuteAsync();

        var row = tb.Rows.Single(r => r.AccountId == parent.Id);
        Assert.True(row.HasChildren);
        Assert.Equal(0m, row.Balance);             // own figure unchanged
        Assert.Equal(300m, row.RolledUpBalance);   // with the child beneath it
        Assert.Equal(tb.GrandTotalDebits, tb.GrandTotalCredits);
    }
}