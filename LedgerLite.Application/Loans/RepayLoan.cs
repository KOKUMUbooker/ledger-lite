using LedgerLite.Application.Common;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   
using LedgerLite.Application.Savings;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Loans;

public record RepayLoanCommand(
    int LoanId, decimal Amount, DateOnly Date, string? Narration = null, string? IdempotencyKey = null);

public record RepayLoanResult(JournalEntry Entry, bool WasDuplicate, decimal InterestPaid, decimal PrincipalPaid);

public class RepayLoan
{
    private readonly LedgerDbContext _db;
    private readonly PostJournalEntry _post;
    private readonly SystemAccountCodes _codes;

    public RepayLoan(LedgerDbContext db, PostJournalEntry post, SystemAccountCodes codes)
    {
        _db = db;
        _post = post;
        _codes = codes;
    }

    public async Task<RepayLoanResult> ExecuteAsync(RepayLoanCommand c, CancellationToken ct = default)
    {
        SavingsRules.ValidateAmount(c.Amount);

        var loan = await LoanRules.RequireAsync(_db, c.LoanId, ct);
        if (c.Date < loan.DisbursedOn)
            throw new InvalidLoanOperationException("A repayment cannot be dated before the loan was disbursed.");

        var cash = await SavingsRules.RequireByCodeAsync(_db, _codes.Cash, ct);

        // Lock first, then read the balances: two simultaneous repayments can't both fit under the same balance
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RowLock.AccountAsync(_db, loan.PrincipalAccountId, ct);

        var principalOutstanding = await LoanRules.BalanceAsync(_db, loan.PrincipalAccountId, null, ct);
        var interestOutstanding = await LoanRules.BalanceAsync(_db, loan.InterestAccountId, null, ct);

        var key = string.IsNullOrWhiteSpace(c.IdempotencyKey) ? null : c.IdempotencyKey.Trim();
        var isReplay = key is not null && await _db.JournalEntries.AnyAsync(e => e.IdempotencyKey == key, ct);

        if (!isReplay && c.Amount > principalOutstanding + interestOutstanding)
            throw new OverpaymentException(principalOutstanding + interestOutstanding, c.Amount);

        var interestPart = Math.Min(c.Amount, Math.Max(interestOutstanding, 0m));
        var principalPart = c.Amount - interestPart;

        var lines = new List<PostLine> { new(cash.Id, c.Amount, 0m) };
        if (interestPart > 0m) lines.Add(new PostLine(loan.InterestAccountId, 0m, interestPart));
        if (principalPart > 0m) lines.Add(new PostLine(loan.PrincipalAccountId, 0m, principalPart));

        var posted = await _post.ExecuteAsync(new PostJournalEntryCommand(
            c.Date,
            string.IsNullOrWhiteSpace(c.Narration) ? $"Loan repayment · {loan.CustomerName}" : c.Narration.Trim(),
            $"RPY-{Guid.NewGuid():N}"[..16],
            lines,
            key,
            EntrySource.LoanRepayment), ct);

        await tx.CommitAsync(ct);

        var interestPaid = posted.Entry.Lines.Where(l => l.AccountId == loan.InterestAccountId).Sum(l => l.Credit);
        var principalPaid = posted.Entry.Lines.Where(l => l.AccountId == loan.PrincipalAccountId).Sum(l => l.Credit);
        return new RepayLoanResult(posted.Entry, posted.WasDuplicate, interestPaid, principalPaid);
    }
}