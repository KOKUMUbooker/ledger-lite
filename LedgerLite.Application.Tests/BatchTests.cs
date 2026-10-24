using LedgerLite.Application.Accounts;
using LedgerLite.Application.Batch;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   
using LedgerLite.Application.Loans;
using LedgerLite.Application.Reports;
using LedgerLite.Application.Savings;
using LedgerLite.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LedgerLite.Application.Tests;

public class BatchTests : LedgerTestBase
{
    public BatchTests(LedgerDbFixture fixture) : base(fixture) { }

    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    // A clock that always says 2 Nov 2026, so these tests don't depend on the day they run
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));

    private record Setup(Account Cash, SystemAccountCodes Codes);

    private async Task<Setup> SetupAsync()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var portfolio = await NewAccountAsync(AccountType.Asset);
        var receivable = await NewAccountAsync(AccountType.Asset);
        var income = await NewAccountAsync(AccountType.Income);

        return new Setup(cash, new SystemAccountCodes(
            Cash: cash.Code, LoanPortfolio: portfolio.Code,
            InterestReceivable: receivable.Code, InterestIncome: income.Code));
    }

    private async Task<Loan> DisburseAsync(Setup s, decimal principal)
    {
        await using var db = Fixture.CreateContext();
        return (await new DisburseLoan(db, new CreateAccount(db), new PostJournalEntry(db), s.Codes)
            .ExecuteAsync(new DisburseLoanCommand("Batch Customer", principal, 12m, 12, Oct1))).Loan;
    }

    private async Task RepayAsync(Setup s, int loanId, decimal amount, DateOnly date)
    {
        await using var db = Fixture.CreateContext();
        await new RepayLoan(db, new PostJournalEntry(db), s.Codes)
            .ExecuteAsync(new RepayLoanCommand(loanId, amount, date));
    }

    // Each loan is accrued in its own scope, so the runner needs a service provider that hands out fresh contexts
    private RunMonthEndAccrual Runner(Setup s)
    {
        var services = new ServiceCollection();
        services.AddSingleton(s.Codes);
        services.AddScoped(_ => Fixture.CreateContext());
        services.AddScoped<PostJournalEntry>();
        services.AddScoped<AccrueInterest>();

        var provider = services.BuildServiceProvider();
        return new RunMonthEndAccrual(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<RunMonthEndAccrual>.Instance);
    }

    private async Task<decimal> InterestOwedAsync(int loanId)
    {
        await using var db = Fixture.CreateContext();
        return (await new GetLoanSummary(db).ExecuteAsync(loanId)).InterestOutstanding;
    }

    [Fact]
    public async Task AccruesEveryLoan_AndTotalsTheInterest()
    {
        var s = await SetupAsync();
        var a = await DisburseAsync(s, 10_000m);
        var b = await DisburseAsync(s, 5_000m);

        var result = await Runner(s).ExecuteAsync(new MonthEndAccrualCommand(Oct31, new[] { a.Id, b.Id }));

        Assert.Equal(2, result.Count(AccrualOutcome.Accrued));
        Assert.Equal(150m, result.TotalInterest);
        Assert.Equal(100m, await InterestOwedAsync(a.Id));
        Assert.Equal(50m, await InterestOwedAsync(b.Id));
    }

    [Fact]
    public async Task RunningTwice_PostsNothingNew()
    {
        var s = await SetupAsync();
        var loan = await DisburseAsync(s, 10_000m);
        var cmd = new MonthEndAccrualCommand(Oct31, new[] { loan.Id });

        await Runner(s).ExecuteAsync(cmd);
        var again = await Runner(s).ExecuteAsync(cmd);

        Assert.Equal(1, again.Count(AccrualOutcome.AlreadyAccrued));
        Assert.Equal(0m, again.TotalInterest);
        Assert.Equal(100m, await InterestOwedAsync(loan.Id));
    }

    [Fact]
    public async Task SettledLoan_HasNothingToAccrue()
    {
        var s = await SetupAsync();
        var loan = await DisburseAsync(s, 1_000m);
        await RepayAsync(s, loan.Id, 1_000m, new DateOnly(2026, 10, 15));

        var result = await Runner(s).ExecuteAsync(new MonthEndAccrualCommand(Oct31, new[] { loan.Id }));

        Assert.Equal(1, result.Count(AccrualOutcome.NothingToAccrue));
        Assert.Equal(0m, await InterestOwedAsync(loan.Id));
    }

    [Fact]
    public async Task OneFailingLoan_DoesNotStopTheOthers()
    {
        var s = await SetupAsync();
        var broken = await DisburseAsync(s, 10_000m);
        var healthy = await DisburseAsync(s, 5_000m);

        // The broken loan's interest account is switched off: posting to it must fail
        await using (var db = Fixture.CreateContext())
            await new SetAccountActive(db).ExecuteAsync(new SetAccountActiveCommand(broken.InterestAccountId, false));

        var result = await Runner(s).ExecuteAsync(new MonthEndAccrualCommand(Oct31, new[] { broken.Id, healthy.Id }));

        var failed = Assert.Single(result.Loans, l => l.Outcome == AccrualOutcome.Failed);
        Assert.Equal(broken.Id, failed.LoanId);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));

        Assert.Single(result.Loans, l => l.Outcome == AccrualOutcome.Accrued && l.LoanId == healthy.Id);
        Assert.Equal(50m, await InterestOwedAsync(healthy.Id));
        Assert.Equal(0m, await InterestOwedAsync(broken.Id));
    }

    [Fact]
    public async Task LoanDisbursedAfterThePeriodEnd_IsLeftAlone()
    {
        var s = await SetupAsync();
        var loan = await DisburseAsync(s, 10_000m);   // disbursed Oct 1

        var result = await Runner(s).ExecuteAsync(new MonthEndAccrualCommand(new DateOnly(2026, 9, 30), new[] { loan.Id }));

        Assert.Empty(result.Loans);
    }

    [Fact]
    public async Task FuturePeriodEnd_IsRejected()
    {
        var s = await SetupAsync();

        await Assert.ThrowsAsync<InvalidLoanOperationException>(() =>
            Runner(s).ExecuteAsync(new MonthEndAccrualCommand(new DateOnly(2026, 12, 31))));
    }

    [Fact]
    public async Task PeriodEnd_OneDayAhead_IsAllowed()
    {
        var s = await SetupAsync();

        // 3 Nov is tomorrow relative to the fixed clock: accepted (no loans selected, so an empty result)
        var result = await Runner(s).ExecuteAsync(
            new MonthEndAccrualCommand(new DateOnly(2026, 11, 3), new[] { int.MaxValue }));

        Assert.Empty(result.Loans);
    }

    private async Task<MonthEndPreview> PreviewMonthEndAsync(Setup s, DateOnly periodEnd, params int[] loanIds)
    {
        await using var db = Fixture.CreateContext();
        var accrue = new AccrueInterest(db, new PostJournalEntry(db), s.Codes);
        return await new PreviewMonthEndAccrual(accrue, db, Clock)
            .ExecuteAsync(new MonthEndAccrualCommand(periodEnd, loanIds));
    }

    private static AccrualPreviewOutcome ExpectedPreview(AccrualOutcome o) => o switch
    {
        AccrualOutcome.Accrued => AccrualPreviewOutcome.WouldAccrue,
        AccrualOutcome.AlreadyAccrued => AccrualPreviewOutcome.AlreadyAccrued,
        AccrualOutcome.NothingToAccrue => AccrualPreviewOutcome.NothingToAccrue,
        _ => AccrualPreviewOutcome.WouldFail
    };

    [Fact]
    public async Task Preview_ShowsWhatEachLoanWouldAccrue_AndPostsNothing()
    {
        var s = await SetupAsync();
        var a = await DisburseAsync(s, 10_000m);
        var b = await DisburseAsync(s, 5_000m);

        var preview = await PreviewMonthEndAsync(s, Oct31, a.Id, b.Id);

        Assert.Equal(2, preview.Count(AccrualPreviewOutcome.WouldAccrue));
        Assert.Equal(150m, preview.TotalInterest);

        var rowA = preview.Loans.Single(l => l.LoanId == a.Id);
        Assert.Equal(10_000m, rowA.PrincipalAtPeriodEnd);
        Assert.Equal(12m, rowA.AnnualRatePercent);
        Assert.Equal(100m, rowA.Amount);
        Assert.Equal(0m, rowA.InterestOutstandingBefore);
        Assert.Equal(100m, rowA.InterestOutstandingAfter);

        // Nothing was posted
        Assert.Equal(0m, await InterestOwedAsync(a.Id));
        Assert.Equal(0m, await InterestOwedAsync(b.Id));
    }

    [Fact]
    public async Task Preview_AgreesWithTheRealRun_LoanByLoan()
    {
        var s = await SetupAsync();
        var broken = await DisburseAsync(s, 10_000m);
        var healthy = await DisburseAsync(s, 5_000m);
        var settled = await DisburseAsync(s, 1_000m);
        await RepayAsync(s, settled.Id, 1_000m, new DateOnly(2026, 10, 15));

        await using (var db = Fixture.CreateContext())
            await new SetAccountActive(db).ExecuteAsync(new SetAccountActiveCommand(broken.InterestAccountId, false));

        var ids = new[] { broken.Id, healthy.Id, settled.Id };

        var preview = await PreviewMonthEndAsync(s, Oct31, ids);
        var run = await Runner(s).ExecuteAsync(new MonthEndAccrualCommand(Oct31, ids));

        Assert.Equal(run.Loans.Count, preview.Loans.Count);
        foreach (var p in preview.Loans)
        {
            var r = run.Loans.Single(x => x.LoanId == p.LoanId);
            Assert.Equal(ExpectedPreview(r.Outcome), p.Outcome);
            Assert.Equal(r.Amount, p.Amount);
            Assert.Equal(r.Error, p.Error);   // the same failure message, not just the same verdict
        }

        // After the run, a second preview must say the healthy loan is already done
        var again = await PreviewMonthEndAsync(s, Oct31, ids);
        Assert.Equal(AccrualPreviewOutcome.AlreadyAccrued, again.Loans.Single(l => l.LoanId == healthy.Id).Outcome);
    }

    [Fact]
    public async Task Preview_RejectsAFuturePeriodEnd_LikeTheRun()
    {
        var s = await SetupAsync();

        await Assert.ThrowsAsync<InvalidLoanOperationException>(() =>
            PreviewMonthEndAsync(s, new DateOnly(2026, 12, 31)));
    }
}