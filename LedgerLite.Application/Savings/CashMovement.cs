namespace LedgerLite.Application.Savings;

public record CashMovementCommand(
    int SavingsAccountId, decimal Amount, DateOnly Date,
    string? Narration = null, string? IdempotencyKey = null);