using LedgerLite.Application.Accounts;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Savings;

public record OpenSavingsAccountCommand(string CustomerName);

public class OpenSavingsAccount
{
    private readonly LedgerDbContext _db;
    private readonly CreateAccount _createAccount;
    private readonly SystemAccountCodes _codes;

    public OpenSavingsAccount(LedgerDbContext db, CreateAccount createAccount, SystemAccountCodes codes)
    {
        _db = db;
        _createAccount = createAccount;
        _codes = codes;
    }

    public async Task<Account> ExecuteAsync(OpenSavingsAccountCommand command, CancellationToken ct = default)
    {
        var customer = (command.CustomerName ?? "").Trim();
        if (customer.Length is 0 or > 80)
            throw new ArgumentException("Customer name is required (max 80 characters).", nameof(command));

        var parent = await SavingsRules.RequireByCodeAsync(_db, _codes.CustomerDeposits, ct);

        // SAV-000001, SAV-000002 ... one per customer under the Customer Deposits parent
        var next = await _db.Accounts.CountAsync(a => a.ParentAccountId == parent.Id, ct) + 1;
        var code = $"SAV-{next:D6}";

        return await _createAccount.ExecuteAsync(
            new CreateAccountCommand(code, $"Savings · {customer}", AccountType.Liability, parent.Id), ct);
    }
}