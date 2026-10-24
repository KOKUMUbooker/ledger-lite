using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Loans;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Batch;

public enum AccrualPreviewOutcome { WouldAccrue, AlreadyAccrued, NothingToAccrue, WouldFail }

public record LoanAccrualPreview(
    int LoanId, string CustomerName, AccrualPreviewOutcome Outcome,
    decimal PrincipalAtPeriodEnd, decimal AnnualRatePercent, decimal Amount,
    decimal InterestOutstandingBefore, decimal InterestOutstandingAfter, string? Error);

public record MonthEndPreview(DateOnly PeriodEnd, IReadOnlyList<LoanAccrualPreview> Loans)
{
    public int Count(AccrualPreviewOutcome o) => Loans.Count(l => l.Outcome == o);
    public decimal TotalInterest => Loans.Where(l => l.Outcome == AccrualPreviewOutcome.WouldAccrue).Sum(l => l.Amount);
}

/// Shows what a month-end run would do, loan by loan, without posting anything.
/// It asks the real accrual code (the plan) and the real posting checks, so it can't disagree with the run.
public class PreviewMonthEndAccrual
{
    private readonly AccrueInterest _accrue;
    private readonly LedgerDbContext _db;
    private readonly TimeProvider _clock;

    public PreviewMonthEndAccrual(AccrueInterest accrue, LedgerDbContext db, TimeProvider clock)
    {
        _accrue = accrue;
        _db = db;
        _clock = clock;
    }

    public async Task<MonthEndPreview> ExecuteAsync(MonthEndAccrualCommand c, CancellationToken ct = default)
    {
        MonthEndRules.ValidatePeriodEnd(_clock, c.PeriodEnd);

        var loans = await MonthEndRules.ListLoansAsync(_db, c, ct);

        // Interest each loan owes right now, in one grouped query
        var owed = (await LoanRules.SummariesAsync(_db, loans, ct))
            .ToDictionary(s => s.Loan.Id, s => s.InterestOutstanding);

        var rows = new List<LoanAccrualPreview>(loans.Count);
        foreach (var loan in loans)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(await PreviewOneAsync(loan, c.PeriodEnd, owed[loan.Id], ct));
        }

        return new MonthEndPreview(c.PeriodEnd, rows);
    }

    private async Task<LoanAccrualPreview> PreviewOneAsync(Loan loan, DateOnly periodEnd, decimal before, CancellationToken ct)
    {
        LoanAccrualPreview Row(AccrualPreviewOutcome outcome, decimal principal, decimal amount, string? error = null) =>
            new(loan.Id, loan.CustomerName, outcome, principal, loan.AnnualRatePercent, amount, before,
                outcome == AccrualPreviewOutcome.WouldAccrue ? before + amount : before, error);

        try
        {
            var plan = await _accrue.PlanAsync(loan.Id, periodEnd, ct);

            if (plan.Command is null)
                return Row(AccrualPreviewOutcome.NothingToAccrue, plan.PrincipalAtPeriodEnd, 0m);

            // The same checks posting runs: already accrued? accounts active?
            var check = await JournalEntryPlanner.PlanAsync(_db, plan.Command, ct);

            if (check.Existing is { } existing)
            {
                var done = existing.Lines.Where(l => l.AccountId == loan.InterestAccountId).Sum(l => l.Debit);
                return Row(AccrualPreviewOutcome.AlreadyAccrued, plan.PrincipalAtPeriodEnd, done);
            }

            return Row(AccrualPreviewOutcome.WouldAccrue, plan.PrincipalAtPeriodEnd, plan.Amount);
        }
        catch (LedgerApplicationException ex)
        {
            // The same business-rule failure the real run would report for this loan
            return Row(AccrualPreviewOutcome.WouldFail, 0m, 0m, ex.Message);
        }
    }
}