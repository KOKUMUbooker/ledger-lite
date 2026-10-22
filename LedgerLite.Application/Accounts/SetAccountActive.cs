using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Accounts;

public record SetAccountActiveCommand(int AccountId, bool IsActive);

public class SetAccountActive
{
    private readonly LedgerDbContext _db;

    public SetAccountActive(LedgerDbContext db) => _db = db;

    public async Task<Account> ExecuteAsync(SetAccountActiveCommand command, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == command.AccountId, ct)
            ?? throw new AccountNotFoundException(command.AccountId);

        if (command.IsActive)
        {
            // An active child under an inactive parent would be a contradiction
            if (account.ParentAccountId is int parentId)
            {
                var parent = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == parentId, ct);
                if (parent is { IsActive: false })
                    throw new InvalidParentAccountException(
                        $"Parent account {parent.Code} is inactive. Activate it first.");
            }

            account.Activate();
        }
        else if (account.IsActive)   // already inactive: nothing to check and nothing to change
        {
            if (await _db.Accounts.AnyAsync(a => a.ParentAccountId == account.Id && a.IsActive, ct))
                throw new AccountHasActiveChildrenException(account.Code);

            // Balance is derived from lines, as everywhere else
            var lines = _db.JournalLines.AsNoTracking().Where(l => l.AccountId == account.Id);
            var debits = await lines.SumAsync(l => l.Debit, ct);
            var credits = await lines.SumAsync(l => l.Credit, ct);
            var balance = account.BalanceFrom(debits, credits);

            if (balance != 0m)
                throw new AccountHasBalanceException(account.Code, balance);

            account.Deactivate();
        }

        await _db.SaveChangesAsync(ct);
        return account;
    }
}