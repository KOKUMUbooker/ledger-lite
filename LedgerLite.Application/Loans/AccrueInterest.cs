using LedgerLite.Application.Common;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   // adjust namespace
using LedgerLite.Application.Savings;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Loans;

public record AccrueInterestCommand(int LoanId, DateOnly PeriodEnd);

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
        var loan = await LoanRules.RequireAsync(_db, c.LoanId, ct);

        if (c.PeriodEnd < loan.DisbursedOn)
            throw new InvalidLoanOperationException("Cannot accrue interest before the loan was disbursed.");

        // Interest is earned on what was outstanding at period end
        var principal = await LoanRules.BalanceAsync(_db, loan.PrincipalAccountId, c.PeriodEnd, ct);

        // Rounding policy.
        var interest = Rounding.MonthlyInterest(principal, loan.AnnualRatePercent);
        if (interest <= 0m) return null;

        var income = await SavingsRules.RequireByCodeAsync(_db, _codes.InterestIncome, ct);
        var period = c.PeriodEnd.ToString("yyyyMM");

        // One accrual per loan per month: running it twice returns the first one
        return await _post.ExecuteAsync(new PostJournalEntryCommand(
            c.PeriodEnd,
            $"Interest accrual {c.PeriodEnd:yyyy-MM} · {loan.CustomerName}",
            $"ACR-{loan.Id}-{period}",
            new[]
            {
                new PostLine(loan.InterestAccountId, interest, 0m),   // Dr interest receivable
                new PostLine(income.Id, 0m, interest)                 // Cr interest income
            },
            $"accrual-{loan.Id}-{period}",
            EntrySource.InterestAccrual), ct);
    }
}