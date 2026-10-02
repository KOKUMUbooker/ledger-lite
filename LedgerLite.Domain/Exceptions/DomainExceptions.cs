namespace LedgerLite.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class InvalidAccountException : DomainException
{
    public InvalidAccountException(string message) : base(message) { }
}

public class InvalidJournalEntryException : DomainException
{
    public InvalidJournalEntryException(string message) : base(message) { }
}

public class UnbalancedEntryException : DomainException
{
    public decimal TotalDebits { get; }
    public decimal TotalCredits { get; }

    public UnbalancedEntryException(decimal totalDebits, decimal totalCredits)
        : base($"Entry is unbalanced: debits {totalDebits:N2} != credits {totalCredits:N2}.")
    {
        TotalDebits = totalDebits;
        TotalCredits = totalCredits;
    }
}