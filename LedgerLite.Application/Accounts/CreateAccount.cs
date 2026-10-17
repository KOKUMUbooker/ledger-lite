using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Accounts;

public record CreateAccountCommand(string Code, string Name, AccountType Type, int? ParentAccountId = null);

public class CreateAccount
{
    private readonly LedgerDbContext _db;

    public CreateAccount(LedgerDbContext db) => _db = db;

    public async Task<Account> ExecuteAsync(CreateAccountCommand command, CancellationToken ct = default)
    {
        var code = command.Code?.Trim() ?? string.Empty;

        if (code.Length > 0 && await _db.Accounts.AnyAsync(a => a.Code == code, ct))
            throw new DuplicateAccountCodeException(code);

        if (command.ParentAccountId is int parentId)
        {
            var parent = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == parentId, ct)
                ?? throw new InvalidParentAccountException($"Parent account {parentId} does not exist.");

            // A control account and its children must be the same kind of account
            if (parent.Type != command.Type)
                throw new InvalidParentAccountException(
                    $"Parent account {parent.Code} is {parent.Type}; a child must be the same type.");
        }

        var account = Account.Create(command.Code!, command.Name, command.Type, command.ParentAccountId);
        _db.Accounts.Add(account);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent create: the unique index caught it
            if (await _db.Accounts.AnyAsync(a => a.Code == account.Code, ct))
                throw new DuplicateAccountCodeException(account.Code);
            throw;
        }

        return account;
    }
}