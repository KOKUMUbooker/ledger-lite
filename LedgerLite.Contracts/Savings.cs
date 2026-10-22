namespace LedgerLite.Contracts;

public record OpenSavingsAccountRequest(string CustomerName);

public record CashMovementRequest(decimal Amount, DateOnly? Date = null, string? Narration = null);