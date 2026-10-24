using LedgerLite.Application.Common;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   // adjust namespace
using LedgerLite.Application.Savings;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Loans;

public record AccrueInterestCommand(int LoanId, DateOnly PeriodEnd);

/// What accruing a loan for a period would involve. Command is null when there is nothing to accrue.
internal record AccrualPlan(Loan Loan, decimal PrincipalAtPeriodEnd, decimal Amount, PostJournalEntryCommand? Command);

public class AccrueInterest
{
    private readonly LedgerDbContext _db;
    private readonly PostJournalEntry _post;
    private readonly SystemAccountCodes _codes;

    public AccrueInterest(LedgerDbContext db, PostJournalEntry post, SystemAccountCodes codes)
    {
        _db = db;
        _post = post;
        _codes = codes;
    }

    /// Returns null when there is nothing to accrue (loan fully repaid by that date).
    public async Task<PostJournalEntryResult?> ExecuteAsync(AccrueInterestCommand c, CancellationToken ct = default)
    {
        var plan = await PlanAsync(c.LoanId, c.PeriodEnd, ct);

        // One accrual per loan per month: running it twice returns the first one
        return plan.Command is null ? null : await _post.ExecuteAsync(plan.Command, ct);
    }

    /// Works out the accrual without posting it. The month-end preview calls this too.
    internal async Task<AccrualPlan> PlanAsync(int loanId, DateOnly periodEnd, CancellationToken ct)
    {
        var loan = await LoanRules.RequireAsync(_db, loanId, ct);

        if (periodEnd < loan.DisbursedOn)
            throw new InvalidLoanOperationException("Cannot accrue interest before the loan was disbursed.");

        // Interest is earned on what was outstanding at period end
        var principal = await LoanRules.BalanceAsync(_db, loan.PrincipalAccountId, periodEnd, ct);
        var interest = Rounding.MonthlyInterest(principal, loan.AnnualRatePercent);

        if (interest <= 0m)
            return new AccrualPlan(loan, principal, 0m, null);

        var income = await SavingsRules.RequireByCodeAsync(_db, _codes.InterestIncome, ct);
        var period = periodEnd.ToString("yyyyMM");

        var command = new PostJournalEntryCommand(
            periodEnd,
            $"Interest accrual {periodEnd:yyyy-MM} · {loan.CustomerName}",
            $"ACR-{loan.Id}-{period}",
            new[]
            {
                new PostLine(loan.InterestAccountId, interest, 0m),   // Dr interest receivable
                new PostLine(income.Id, 0m, interest)                 // Cr interest income
            },
            $"accrual-{loan.Id}-{period}",
            EntrySource.InterestAccrual);

        return new AccrualPlan(loan, principal, interest, command);
    }
}