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
            var code = await NextCodeAsync(ct);
            try
            {
                return await _createAccount.ExecuteAsync(
                    new CreateAccountCommand(code, $"Savings · {customer}", AccountType.Liability, parent.Id), ct);
            }
            catch (DuplicateAccountCodeException) when (attempt < 4)
            {
                // Another request took that number a moment ago: read again and take the next one
            }
        }
    }

    // Codes are unique across ALL accounts, so numbering must be global, not per parent.
    // They are zero-padded, so the highest string is also the highest number.
    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var last = await _db.Accounts.AsNoTracking()
            .Where(a => a.Code.StartsWith(Prefix) && a.Code.Length == Prefix.Length + Digits)
            .OrderByDescending(a => a.Code)
            .Select(a => a.Code)
            .FirstOrDefaultAsync(ct);

        var next = last is not null && int.TryParse(last.AsSpan(Prefix.Length), out var n) ? n + 1 : 1;
        return $"{Prefix}{next.ToString($"D{Digits}")}";
    }
}