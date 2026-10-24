using LedgerLite.Application.Accounts;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   // adjust namespace
using LedgerLite.Application.Loans;
using LedgerLite.Application.Reports;
using LedgerLite.Application.Savings;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class LoanTests : LedgerTestBase
{
    public LoanTests(LedgerDbFixture fixture) : base(fixture) { }

    private static readonly DateOnly Oct1 = new(2026, 10, 1);

    private record Setup(Account Cash, Account Income, SystemAccountCodes Codes);

    private async Task<Setup> SetupAsync()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var portfolio = await NewAccountAsync(AccountType.Asset);
        var receivable = await NewAccountAsync(AccountType.Asset);
        var income = await NewAccountAsync(AccountType.Income);

        return new Setup(cash, income, new SystemAccountCodes(
            Cash: cash.Code, LoanPortfolio: portfolio.Code,
            InterestReceivable: receivable.Code, InterestIncome: income.Code));
    }

    private async Task<DisburseLoanResult> DisburseAsync(
        Setup s, decimal principal = 10_000m, decimal rate = 12m, int months = 12, string? key = null)
    {
        await using var db = Fixture.CreateContext();
        return await new DisburseLoan(db, new CreateAccount(db), new PostJournalEntry(db), s.Codes)
            .ExecuteAsync(new DisburseLoanCommand("Jane Wanjiru", principal, rate, months, Oct1, key));
    }

    private async Task<PostJournalEntryResult?> AccrueAsync(Setup s, int loanId, DateOnly periodEnd)
    {
        await using var db = Fixture.CreateContext();
        return await new AccrueInterest(db, new PostJournalEntry(db), s.Codes)
            .ExecuteAsync(new AccrueInterestCommand(loanId, periodEnd));
    }

    private async Task<RepayLoanResult> RepayAsync(
        Setup s, int loanId, decimal amount, DateOnly? date = null, string? key = null)
    {
        await using var db = Fixture.CreateContext();
        return await new RepayLoan(db, new PostJournalEntry(db), s.Codes)
            .ExecuteAsync(new RepayLoanCommand(loanId, amount, date ?? new DateOnly(2026, 10, 20), null, key));
    }

    private async Task<LoanSummary> SummaryAsync(int loanId)
    {
        await using var db = Fixture.CreateContext();
        return await new GetLoanSummary(db).ExecuteAsync(loanId);
    }

    private async Task<decimal> BalanceAsync(int accountId)
    {
        await using var db = Fixture.CreateContext();
        return (await new GetAccountLedger(db).ExecuteAsync(accountId)).ClosingBalance;
    }

    [Fact]
    public async Task Disburse_PostsPrincipalAndTakesCash()
    {
        var s = await SetupAsync();

        var result = await DisburseAsync(s);
        var summary = await SummaryAsync(result.Loan.Id);

        Assert.Equal(10_000m, summary.PrincipalOutstanding);
        Assert.Equal(0m, summary.InterestOutstanding);
        Assert.Equal(-10_000m, await BalanceAsync(s.Cash.Id));
    }

    [Fact]
    public async Task Disburse_Replay_ReturnsTheSameLoan()
    {
        var s = await SetupAsync();
        var key = $"ldb-{Guid.NewGuid():N}";

        var first = await DisburseAsync(s, key: key);
        var again = await DisburseAsync(s, key: key);

        Assert.True(again.WasDuplicate);
        Assert.Equal(first.Loan.Id, again.Loan.Id);
        Assert.Equal(10_000m, (await SummaryAsync(first.Loan.Id)).PrincipalOutstanding);
    }

    [Theory]
    [InlineData(0, 12, 12)]
    [InlineData(1000, 150, 12)]
    [InlineData(1000, 12, 0)]
    [InlineData(1000.555, 12, 12)]
    public async Task Disburse_InvalidTerms_AreRejected(double principal, double rate, int months)
    {
        var s = await SetupAsync();

        await Assert.ThrowsAsync<InvalidLoanOperationException>(() =>
            DisburseAsync(s, (decimal)principal, (decimal)rate, months));
    }

    [Fact]
    public async Task Accrue_OneMonth_IsPrincipalTimesRateOverTwelve()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;

        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        Assert.Equal(100m, (await SummaryAsync(loan.Id)).InterestOutstanding);
        Assert.Equal(100m, await BalanceAsync(s.Income.Id));
    }

    [Fact]
    public async Task Accrue_Rounds_ToTwoDecimals()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s, principal: 1_000m, rate: 10m)).Loan;

        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        Assert.Equal(8.33m, (await SummaryAsync(loan.Id)).InterestOutstanding);
    }

    [Fact]
    public async Task Accrue_SameMonthTwice_PostsOnce()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;

        var first = await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));
        var again = await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        Assert.False(first!.WasDuplicate);
        Assert.True(again!.WasDuplicate);
        Assert.Equal(100m, (await SummaryAsync(loan.Id)).InterestOutstanding);
    }

    [Fact]
    public async Task Accrue_UsesPrincipalAsOfPeriodEnd()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        await RepayAsync(s, loan.Id, 5_000m, new DateOnly(2026, 10, 15));   // no interest owed yet: all principal

        var oct10 = await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 10));   // before the repayment
        var nov30 = await AccrueAsync(s, loan.Id, new DateOnly(2026, 11, 30));   // after it

        Assert.Equal(100m, oct10!.Entry.Lines.Sum(l => l.Credit));
        Assert.Equal(50m, nov30!.Entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Repay_PaysInterestFirst_ThenPrincipal()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        var result = await RepayAsync(s, loan.Id, 150m);
        var summary = await SummaryAsync(loan.Id);

        Assert.Equal(100m, result.InterestPaid);
        Assert.Equal(50m, result.PrincipalPaid);
        Assert.Equal(9_950m, summary.PrincipalOutstanding);
        Assert.Equal(0m, summary.InterestOutstanding);
        Assert.Equal(-9_850m, await BalanceAsync(s.Cash.Id));   // -10,000 out, +150 in
    }

    [Fact]
    public async Task Repay_LessThanInterest_AllGoesToInterest()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        var result = await RepayAsync(s, loan.Id, 40m);

        Assert.Equal(40m, result.InterestPaid);
        Assert.Equal(0m, result.PrincipalPaid);
        Assert.Equal(60m, (await SummaryAsync(loan.Id)).InterestOutstanding);
    }

    [Fact]
    public async Task Repay_MoreThanOwed_IsRejected_AndPostsNothing()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        await Assert.ThrowsAsync<OverpaymentException>(() => RepayAsync(s, loan.Id, 10_100.01m));

        Assert.Equal(10_100m, (await SummaryAsync(loan.Id)).TotalOutstanding);
    }

    [Fact]
    public async Task Repay_Everything_SettlesTheLoan()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        await AccrueAsync(s, loan.Id, new DateOnly(2026, 10, 31));

        await RepayAsync(s, loan.Id, 10_100m);

        Assert.True((await SummaryAsync(loan.Id)).IsSettled);
    }

    [Fact]
    public async Task Repay_Replay_AfterSettling_ReturnsTheOriginal_NotAnOverpayment()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        var key = $"rpy-{Guid.NewGuid():N}";

        var first = await RepayAsync(s, loan.Id, 10_000m, key: key);
        var again = await RepayAsync(s, loan.Id, 10_000m, key: key);

        Assert.True((await SummaryAsync(loan.Id)).IsSettled);
        Assert.True(again.WasDuplicate);
        Assert.Equal(first.Entry.Id, again.Entry.Id);
        Assert.Equal(10_000m, again.PrincipalPaid);
    }

    [Fact]
    public async Task ReversingARepayment_ReopensTheLoan()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;
        var repayment = await RepayAsync(s, loan.Id, 10_000m);
        Assert.True((await SummaryAsync(loan.Id)).IsSettled);

        await ReverseAsync(repayment.Entry.Id);

        var summary = await SummaryAsync(loan.Id);
        Assert.False(summary.IsSettled);
        Assert.Equal(10_000m, summary.PrincipalOutstanding);
    }

    [Fact]
    public async Task UnknownLoan_IsNotFound()
    {
        var s = await SetupAsync();

        await Assert.ThrowsAsync<LoanNotFoundException>(() => RepayAsync(s, int.MaxValue, 10m));
    }

    [Fact]
    public async Task ConcurrentRepayments_CannotOverpay()
    {
        var s = await SetupAsync();
        var loan = (await DisburseAsync(s)).Loan;   // 10,000 owed

        // Four repayments of 6,000 at once: only one fits
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await RepayAsync(s, loan.Id, 6_000m); return true; }
            catch (OverpaymentException) { return false; }
        }));

        Assert.Equal(1, outcomes.Count(ok => ok));
        Assert.Equal(4_000m, (await SummaryAsync(loan.Id)).PrincipalOutstanding);
    }
}