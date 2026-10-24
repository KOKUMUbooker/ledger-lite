namespace LedgerLite.Contracts;

public record MonthEndRequest(DateOnly PeriodEnd, IReadOnlyList<int>? LoanIds = null);

public record LoanAccrualResultDto(int LoanId, string CustomerName, string Outcome, decimal Amount, string? Error);

public record MonthEndResultDto(
    DateOnly PeriodEnd, int Accrued, int AlreadyAccrued, int NothingToAccrue, int Failed,
    decimal TotalInterest, IReadOnlyList<LoanAccrualResultDto> Loans);

// --------- previews dtos -------------
public record LoanAccrualPreviewDto(
    int LoanId, string CustomerName, string Outcome,
    decimal PrincipalAtPeriodEnd, decimal AnnualRatePercent, decimal Amount,
    decimal InterestOutstandingBefore, decimal InterestOutstandingAfter, string? Error);

public record MonthEndPreviewDto(
    DateOnly PeriodEnd, int WouldAccrue, int AlreadyAccrued, int NothingToAccrue, int WouldFail,
    decimal TotalInterest, IReadOnlyList<LoanAccrualPreviewDto> Loans);