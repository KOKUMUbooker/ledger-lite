using System.Globalization;

namespace LedgerLite.Application.Exceptions;

/// Base type so the Api layer can map all "expected" failures to 4xx responses.
public abstract class LedgerApplicationException : Exception
{
    protected LedgerApplicationException(string message) : base(message) { }
}

public class AccountNotFoundException : LedgerApplicationException
{
    public AccountNotFoundException(int accountId)
        : base($"Account {accountId} does not exist.") { }
}

public class AccountInactiveException : LedgerApplicationException
{
    public AccountInactiveException(string code)
        : base($"Account {code} is inactive and cannot receive postings.") { }
}

public class AccountHasBalanceException : LedgerApplicationException
{
    public AccountHasBalanceException(string code, decimal balance)
        : base($"Account {code} still has a balance of {balance.ToString("N2", CultureInfo.InvariantCulture)}. " +
               "Bring it to zero before deactivating.")
    { }
}

public class AccountHasActiveChildrenException : LedgerApplicationException
{
    public AccountHasActiveChildrenException(string code)
        : base($"Account {code} has active sub-accounts. Deactivate them first.") { }
}

public class DuplicateAccountCodeException : LedgerApplicationException
{
    public DuplicateAccountCodeException(string code)
        : base($"An account with code '{code}' already exists.") { }
}

public class InvalidParentAccountException : LedgerApplicationException
{
    public InvalidParentAccountException(string message) : base(message) { }
}

public class EntryNotFoundException : LedgerApplicationException
{
    public EntryNotFoundException(int entryId)
        : base($"Journal entry {entryId} does not exist.") { }
}

public class EntryAlreadyReversedException : LedgerApplicationException
{
    public EntryAlreadyReversedException(int entryId)
        : base($"Journal entry {entryId} has already been reversed.") { }
}

public class NotASavingsAccountException : LedgerApplicationException
{
    public NotASavingsAccountException(string code)
        : base($"Account {code} is not a customer savings account.") { }
}

public class InsufficientFundsException : LedgerApplicationException
{
    public InsufficientFundsException(string code, decimal balance, decimal amount)
        : base($"Account {code} has {balance.ToString("N2", CultureInfo.InvariantCulture)} available; " +
               $"cannot withdraw {amount.ToString("N2", CultureInfo.InvariantCulture)}.")
    { }
}

public class InvalidCashAmountException : LedgerApplicationException
{
    public InvalidCashAmountException()
        : base("Amount must be greater than zero and have at most 2 decimal places.") { }
}

public class LoanNotFoundException : LedgerApplicationException
{
    public LoanNotFoundException(int id) : base($"Loan {id} was not found.") { }
}

public class OverpaymentException : LedgerApplicationException
{
    public OverpaymentException(decimal outstanding, decimal amount)
        : base($"The loan has {outstanding.ToString("N2", CultureInfo.InvariantCulture)} outstanding; " +
               $"cannot repay {amount.ToString("N2", CultureInfo.InvariantCulture)}.")
    { }
}

public class InvalidLoanOperationException : LedgerApplicationException
{
    public InvalidLoanOperationException(string message) : base(message) { }
}