using LedgerLite.Application.Accounts;
using LedgerLite.Application.Exceptions;
using LedgerLite.Application.Journal;   
using LedgerLite.Application.Savings;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Loans;

public record DisburseLoanCommand(
    string CustomerName, decimal Principal, decimal AnnualRatePercent, int TermMonths,
    DateOnly Date, string? IdempotencyKey = null);

public record DisburseLoanResult(Loan Loan, JournalEntry Entry, bool WasDuplicate);

public class DisburseLoan
{
    private readonly LedgerDbContext _db;
    private readonly CreateAccount _createAccount;
    private readonly PostJournalEntry _post;
    private readonly SystemAccountCodes _codes;

    public DisburseLoan(LedgerDbContext db, CreateAccount createAccount, PostJournalEntry post, SystemAccountCodes codes)
    {
        _db = db;
        _createAccount = createAccount;
        _post = post;
        _codes = codes;
    }

    public async Task<DisburseLoanResult> ExecuteAsync(DisburseLoanCommand c, CancellationToken ct = default)
    {
        var customer = (c.CustomerName ?? "").Trim();
        if (customer.Length is 0 or > 80)
            throw new InvalidLoanOperationException("Customer name is required (max 80 characters).");
        if (c.Principal <= 0m || decimal.Round(c.Principal, 2) != c.Principal)
            throw new InvalidLoanOperationException("Principal must be greater than zero with at most 2 decimal places.");
        if (c.AnnualRatePercent is < 0m or > 100m || decimal.Round(c.AnnualRatePercent, 2) != c.AnnualRatePercent)
            throw new InvalidLoanOperationException("Annual rate must be between 0 and 100 percent.");
        if (c.TermMonths is < 1 or > 360)
            throw new InvalidLoanOperationException("Term must be between 1 and 360 months.");

        var key = string.IsNullOrWhiteSpace(c.IdempotencyKey) ? null : c.IdempotencyKey.Trim();

        // A retried request returns the original loan and entry
        if (key is not null)
        {
            var existing = await _db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.DisbursementKey == key, ct);
            if (existing is not null)
            {
                var entry = await _db.JournalEntries.AsNoTracking().Include(e => e.Lines)
                    .FirstAsync(e => e.IdempotencyKey == key, ct);
                return new DisburseLoanResult(existing, entry, true);
            }
        }

        var portfolio = await SavingsRules.RequireByCodeAsync(_db, _codes.LoanPortfolio, ct);
        var receivable = await SavingsRules.RequireByCodeAsync(_db, _codes.InterestReceivable, ct);
        var cash = await SavingsRules.RequireByCodeAsync(_db, _codes.Cash, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Highest of both series, so the LN- and LI- numbers stay in step and never collide
        var n = Math.Max(
            await AccountCodes.NextNumberAsync(_db, "LN-", 6, ct),
            await AccountCodes.NextNumberAsync(_db, "LI-", 6, ct));

        var principalAccount = await _createAccount.ExecuteAsync(new CreateAccountCommand(
            AccountCodes.Format("LN-", 6, n), $"Loan · {customer}", AccountType.Asset, portfolio.Id), ct);
        var interestAccount = await _createAccount.ExecuteAsync(new CreateAccountCommand(
            AccountCodes.Format("LI-", 6, n), $"Loan interest · {customer}", AccountType.Asset, receivable.Id), ct);

        var loan = Loan.Create(customer, c.Principal, c.AnnualRatePercent, c.TermMonths,
            c.Date, principalAccount.Id, interestAccount.Id, key);
        _db.Loans.Add(loan);   // saved by the same SaveChanges that persists the entry

        // Dr Loan principal (we are owed more), Cr Cash (cash leaves)
        var posted = await _post.ExecuteAsync(new PostJournalEntryCommand(
            c.Date,
            $"Loan disbursement · {customer}",
            $"LDB-{n:D6}",
            new[]
            {
                new PostLine(principalAccount.Id, c.Principal, 0m),
                new PostLine(cash.Id, 0m, c.Principal)
            },
            key,
            EntrySource.LoanDisbursement), ct);

        await tx.CommitAsync(ct);
        return new DisburseLoanResult(loan, posted.Entry, false);
    }
}