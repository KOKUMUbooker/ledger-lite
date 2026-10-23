using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Savings;

internal static class SavingsRules
{
    // A missing system account is a setup error, not the caller's mistake: let it surface as a 500.
    public static async Task<Account> RequireByCodeAsync(LedgerDbContext db, string code, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct)
        ?? throw new InvalidOperationException($"System account '{code}' does not exist. Check SystemAccountCodes.");

    public static async Task<(Account Savings, Account Cash)> LoadAsync(
        LedgerDbContext db, SystemAccountCodes codes, int savingsAccountId, CancellationToken ct)
    {
        var parent = await RequireByCodeAsync(db, codes.CustomerDeposits, ct);
        var cash = await RequireByCodeAsync(db, codes.Cash, ct);

        var savings = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == savingsAccountId, ct)
            ?? throw new AccountNotFoundException(savingsAccountId);

        if (savings.ParentAccountId != parent.Id)
            throw new NotASavingsAccountException(savings.Code);

        return (savings, cash);
    }

    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0m || decimal.Round(amount, 2) != amount)
            throw new InvalidCashAmountException();
    }

    public static async Task<decimal> BalanceAsync(LedgerDbContext db, Account account, CancellationToken ct)
    {
        var lines = db.JournalLines.AsNoTracking().Where(l => l.AccountId == account.Id);
        return account.BalanceFrom(
            await lines.SumAsync(l => l.Debit, ct),
            await lines.SumAsync(l => l.Credit, ct));
    }
}