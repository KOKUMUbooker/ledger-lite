using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Loans;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedgerLite.Application.Batch;

/// LoanIds null = every loan. Passing ids lets you re-run just the ones that failed.
public record MonthEndAccrualCommand(DateOnly PeriodEnd, IReadOnlyCollection<int>? LoanIds = null);

public enum AccrualOutcome { Accrued, AlreadyAccrued, NothingToAccrue, Failed }

public record LoanAccrualResult(int LoanId, string CustomerName, AccrualOutcome Outcome, decimal Amount, string? Error);

public record MonthEndAccrualResult(DateOnly PeriodEnd, IReadOnlyList<LoanAccrualResult> Loans)
{
    public int Count(AccrualOutcome o) => Loans.Count(l => l.Outcome == o);
    public decimal TotalInterest => Loans.Where(l => l.Outcome == AccrualOutcome.Accrued).Sum(l => l.Amount);
}

public class RunMonthEndAccrual
{
    // One run at a time. This assumes a single API instance; with several you would use a database lock.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<RunMonthEndAccrual> _log;

    public RunMonthEndAccrual(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RunMonthEndAccrual> log)
    {
        _scopes = scopes;
        _clock = clock;
        _log = log;
    }

    public async Task<MonthEndAccrualResult> ExecuteAsync(MonthEndAccrualCommand c, CancellationToken ct = default)
    {
        // Catches typos like 2062-10-31 before they accrue interest into the future
        MonthEndRules.ValidatePeriodEnd(_clock, c.PeriodEnd);

        if (!await Gate.WaitAsync(0, ct))
            throw new BatchAlreadyRunningException();

        try
        {
            List<Loan> loans;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                loans = await MonthEndRules.ListLoansAsync(
                    scope.ServiceProvider.GetRequiredService<LedgerDbContext>(), c, ct);
            }
            var results = new List<LoanAccrualResult>(loans.Count);

            foreach (var loan in loans)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await AccrueOneAsync(loan, c.PeriodEnd, ct));
            }

            return new MonthEndAccrualResult(c.PeriodEnd, results);
        }
        finally
        {
            Gate.Release();
        }
    }

    // Each loan gets its own scope, and so its own DbContext: a failure can leave a half-added entry
    // tracked in a context, and that must never leak into the next loan.
    private async Task<LoanAccrualResult> AccrueOneAsync(Loan loan, DateOnly periodEnd, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var accrue = scope.ServiceProvider.GetRequiredService<AccrueInterest>();

            var posted = await accrue.ExecuteAsync(new AccrueInterestCommand(loan.Id, periodEnd), ct);
            if (posted is null)
                return new LoanAccrualResult(loan.Id, loan.CustomerName, AccrualOutcome.NothingToAccrue, 0m, null);

            var amount = posted.Entry.Lines.Where(l => l.AccountId == loan.InterestAccountId).Sum(l => l.Debit);
            return new LoanAccrualResult(
                loan.Id, loan.CustomerName,
                posted.WasDuplicate ? AccrualOutcome.AlreadyAccrued : AccrualOutcome.Accrued,
                amount, null);
        }
        catch (LedgerApplicationException ex)
        {
            // A business-rule failure (e.g. the loan's interest account is inactive): report it and carry on
            return new LoanAccrualResult(loan.Id, loan.CustomerName, AccrualOutcome.Failed, 0m, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Month-end accrual failed unexpectedly for loan {LoanId}", loan.Id);
            return new LoanAccrualResult(loan.Id, loan.CustomerName, AccrualOutcome.Failed, 0m,
                "Unexpected error. See the server log.");
        }
    }
}