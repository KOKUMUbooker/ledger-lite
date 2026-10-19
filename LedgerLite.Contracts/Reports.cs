namespace LedgerLite.Contracts;

public record LedgerLineDto(
    DateOnly Date, int EntryId, string Description, string Reference,
    decimal Debit, decimal Credit, decimal RunningBalance);

public record AccountLedgerDto(
    int AccountId, string Code, string Name, string Type, string NormalBalance,
    DateOnly? From, DateOnly? To,
    decimal OpeningBalance,
    IReadOnlyList<LedgerLineDto> Lines,
    decimal TotalDebits, decimal TotalCredits,
    decimal ClosingBalance);

public record TrialBalanceRowDto(
    int AccountId, string Code, string Name, string Type,
    decimal TotalDebits, decimal TotalCredits, decimal Balance);

public record TrialBalanceDto(
    DateOnly? AsOf,
    IReadOnlyList<TrialBalanceRowDto> Rows,
    decimal GrandTotalDebits,
    decimal GrandTotalCredits,
    bool IsBalanced);