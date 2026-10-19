namespace LedgerLite.Contracts;

public record AccountDto(
    int Id, string Code, string Name, string Type, string NormalBalance,
    int? ParentAccountId, bool IsActive);

public record CreateAccountRequest(string Code, string Name, string Type, int? ParentAccountId = null);