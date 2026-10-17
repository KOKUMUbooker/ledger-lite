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