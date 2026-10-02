namespace LedgerLite.Domain.Entities;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}

public static class AccountTypeExtensions
{
    /// Assets and expenses increase with a debit. Everything else increases with a credit.
    public static bool IsDebitNormal(this AccountType type) =>
        type is AccountType.Asset or AccountType.Expense;
}