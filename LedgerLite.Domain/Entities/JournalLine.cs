namespace LedgerLite.Domain.Entities;

public class JournalLine
{
    private JournalLine() { } // for EF Core

    public int Id { get; private set; }
    public int JournalEntryId { get; private set; }
    public int AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }

    public static JournalLine ForDebit(int accountId, decimal amount) =>
        new() { AccountId = accountId, Debit = amount };

    public static JournalLine ForCredit(int accountId, decimal amount) =>
        new() { AccountId = accountId, Credit = amount };

    /// Same account, opposite side. Used to build reversals.
    internal JournalLine Flipped() =>
        new() { AccountId = AccountId, Debit = Credit, Credit = Debit };
}