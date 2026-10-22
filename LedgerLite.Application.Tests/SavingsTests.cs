using LedgerLite.Application.Accounts;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Reports;
using LedgerLite.Application.Journal;   
using LedgerLite.Application.Savings;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class SavingsTests : LedgerTestBase
{
    public SavingsTests(LedgerDbFixture fixture) : base(fixture) { }

    private record Setup(Account Cash, Account Deposits, Account Savings, SystemAccountCodes Codes);

    private async Task<Setup> SetupAsync()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var codes = new SystemAccountCodes(cash.Code, deposits.Code);

        await using var db = Fixture.CreateContext();
        var savings = await new OpenSavingsAccount(db, new CreateAccount(db), codes)
            .ExecuteAsync(new OpenSavingsAccountCommand("Jane Wanjiru"));

        return new Setup(cash, deposits, savings, codes);
    }

    private async Task<PostJournalEntryResult> DepositAsync(Setup s, decimal amount, string? key = null)
    {
        await using var db = Fixture.CreateContext();
        return await new DepositCash(db, new PostJournalEntry(db), s.Codes).ExecuteAsync(
            new CashMovementCommand(s.Savings.Id, amount, new DateOnly(2026, 10, 20), IdempotencyKey: key));
    }

    private async Task<PostJournalEntryResult> WithdrawAsync(Setup s, decimal amount, string? key = null)
    {
        await using var db = Fixture.CreateContext();
        return await new WithdrawCash(db, new PostJournalEntry(db), s.Codes).ExecuteAsync(
            new CashMovementCommand(s.Savings.Id, amount, new DateOnly(2026, 10, 20), IdempotencyKey: key));
    }

    private async Task<decimal> BalanceOfAsync(int accountId)
    {
        await using var db = Fixture.CreateContext();
        return (await new GetAccountLedger(db).ExecuteAsync(accountId)).ClosingBalance;
    }

    [Fact]
    public async Task OpenSavingsAccount_CreatesLiabilityUnderCustomerDeposits()
    {
        var s = await SetupAsync();

        Assert.Equal(AccountType.Liability, s.Savings.Type);
        Assert.Equal(s.Deposits.Id, s.Savings.ParentAccountId);
        Assert.StartsWith("SAV-", s.Savings.Code);
    }

    [Fact]
    public async Task Deposit_MovesCashAndCustomerBalanceTogether()
    {
        var s = await SetupAsync();

        await DepositAsync(s, 5_000m);

        Assert.Equal(5_000m, await BalanceOfAsync(s.Cash.Id));
        Assert.Equal(5_000m, await BalanceOfAsync(s.Savings.Id));
    }

    [Fact]
    public async Task Withdraw_ReducesBoth()
    {
        var s = await SetupAsync();
        await DepositAsync(s, 5_000m);

        await WithdrawAsync(s, 1_200m);

        Assert.Equal(3_800m, await BalanceOfAsync(s.Cash.Id));
        Assert.Equal(3_800m, await BalanceOfAsync(s.Savings.Id));
    }

    [Fact]
    public async Task Withdraw_MoreThanBalance_IsRejected_AndPostsNothing()
    {
        var s = await SetupAsync();
        await DepositAsync(s, 500m);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => WithdrawAsync(s, 500.01m));

        Assert.Equal(500m, await BalanceOfAsync(s.Savings.Id));
    }

    [Fact]
    public async Task Withdraw_ExactBalance_IsAllowed()
    {
        var s = await SetupAsync();
        await DepositAsync(s, 500m);

        await WithdrawAsync(s, 500m);

        Assert.Equal(0m, await BalanceOfAsync(s.Savings.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public async Task BadAmounts_AreRejected(double amount)
    {
        var s = await SetupAsync();

        await Assert.ThrowsAsync<InvalidCashAmountException>(() => DepositAsync(s, (decimal)amount));
    }

    [Fact]
    public async Task Deposit_ToAnAccountThatIsNotSavings_IsRejected()
    {
        var s = await SetupAsync();
        var other = await NewAccountAsync(AccountType.Liability);   // not under Customer Deposits

        await using var db = Fixture.CreateContext();
        var deposit = new DepositCash(db, new PostJournalEntry(db), s.Codes);

        await Assert.ThrowsAsync<NotASavingsAccountException>(() =>
            deposit.ExecuteAsync(new CashMovementCommand(other.Id, 100m, new DateOnly(2026, 10, 20))));
    }

    [Fact]
    public async Task Deposit_WithSameIdempotencyKey_PostsOnce()
    {
        var s = await SetupAsync();
        var key = $"dep-{Guid.NewGuid():N}";

        var first = await DepositAsync(s, 700m, key);
        var again = await DepositAsync(s, 700m, key);

        Assert.False(first.WasDuplicate);
        Assert.True(again.WasDuplicate);
        Assert.Equal(first.Entry.Id, again.Entry.Id);
        Assert.Equal(700m, await BalanceOfAsync(s.Savings.Id));
    }

    [Fact]
    public async Task Withdraw_Replay_AfterBalanceIsGone_ReturnsTheOriginal_NotAnError()
    {
        var s = await SetupAsync();
        await DepositAsync(s, 500m);
        var key = $"wdr-{Guid.NewGuid():N}";

        var first = await WithdrawAsync(s, 500m, key);   // balance is now zero
        var again = await WithdrawAsync(s, 500m, key);   // same request retried

        Assert.True(again.WasDuplicate);
        Assert.Equal(first.Entry.Id, again.Entry.Id);
        Assert.Equal(0m, await BalanceOfAsync(s.Savings.Id));
    }

    [Fact]
    public async Task ReversingADeposit_RestoresBothBalances()
    {
        var s = await SetupAsync();
        var deposit = await DepositAsync(s, 900m);

        await ReverseAsync(deposit.Entry.Id);

        Assert.Equal(0m, await BalanceOfAsync(s.Cash.Id));
        Assert.Equal(0m, await BalanceOfAsync(s.Savings.Id));
    }
}