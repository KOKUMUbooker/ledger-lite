using LedgerLite.Application.Accounts;
using LedgerLite.Application.Exceptions;
using LedgerLite.Data;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Savings;

public record OpenSavingsAccountCommand(string CustomerName);

public class OpenSavingsAccount
{
    private const string Prefix = "SAV-";
    private const int Digits = 6;

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

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var number = await AccountCodes.NextNumberAsync(_db, "SAV-", 6, ct);
                var code = AccountCodes.Format("SAV-", 6, number);
                return await _createAccount.ExecuteAsync(
                    new CreateAccountCommand(code, $"Savings · {customer}", AccountType.Liability, parent.Id), ct);
            }
            catch (DuplicateAccountCodeException) when (attempt < 4)
            {
                // Another request took that number a moment ago: read again and take the next one
            }
        }
    }
 }