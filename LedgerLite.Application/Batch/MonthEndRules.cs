using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Batch;

internal static class MonthEndRules
{
    /// Catches typos like 2062-10-31 before they accrue interest into the future.
    public static void ValidatePeriodEnd(TimeProvider clock, DateOnly periodEnd)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (periodEnd > today.AddDays(1))
            throw new InvalidLoanOperationException("Period end cannot be in the future.");
    }

    /// The loans a month-end run covers: disbursed by the period end, optionally limited to some ids.
    public static Task<List<Loan>> ListLoansAsync(LedgerDbContext db, MonthEndAccrualCommand c, CancellationToken ct)
    {
        var query = db.Loans.AsNoTracking().Where(l => l.DisbursedOn <= c.PeriodEnd);
        if (c.LoanIds is { Count: > 0 } ids) query = query.Where(l => ids.Contains(l.Id));

        return query.OrderBy(l => l.Id).ToListAsync(ct);
    }
}