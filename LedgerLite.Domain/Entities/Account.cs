using LedgerLite.Domain.Exceptions;

namespace LedgerLite.Domain.Entities;

public class Account
{
    private Account() { } // for EF Core

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AccountType Type { get; private set; }
    public int? ParentAccountId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Account Create(string code, string name, AccountType type, int? parentAccountId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidAccountException("Account code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidAccountException("Account name is required.");

        return new Account
        {
            Code = code.Trim(),
            Name = name.Trim(),
            Type = type,
            ParentAccountId = parentAccountId
        };
    }

    /// Accounts with history are never deleted; they are deactivated so no new postings go to them.
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public decimal BalanceFrom(decimal totalDebits, decimal totalCredits) =>
        Type.IsDebitNormal()
            ? totalDebits - totalCredits
            : totalCredits - totalDebits;
}