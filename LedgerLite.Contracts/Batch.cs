namespace LedgerLite.Contracts;

public record MonthEndRequest(DateOnly PeriodEnd, IReadOnlyList<int>? LoanIds = null);

public record LoanAccrualResultDto(int LoanId, string CustomerName, string Outcome, decimal Amount, string? Error);

public record MonthEndResultDto(
    DateOnly PeriodEnd, int Accrued, int AlreadyAccrued, int NothingToAccrue, int Failed,
    decimal TotalInterest, IReadOnlyList<LoanAccrualResultDto> Loans);