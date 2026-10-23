namespace LedgerLite.Contracts;

public record DisburseLoanRequest(
    string CustomerName, decimal Principal, decimal AnnualRatePercent, int TermMonths,
    DateOnly? Date = null, string? IdempotencyKey = null);

public record RepayLoanRequest(
    decimal Amount, DateOnly? Date = null, string? Narration = null, string? IdempotencyKey = null);

public record AccrueInterestRequest(DateOnly PeriodEnd);

public record LoanDto(
    int Id, string CustomerName, decimal PrincipalAmount, decimal AnnualRatePercent, int TermMonths,
    DateOnly DisbursedOn, int PrincipalAccountId, int InterestAccountId);

public record LoanSummaryDto(
    LoanDto Loan, decimal PrincipalOutstanding, decimal InterestOutstanding,
    decimal TotalOutstanding, bool IsSettled);

public record RepaymentDto(JournalEntryDto Entry, decimal InterestPaid, decimal PrincipalPaid, LoanSummaryDto Loan);

/// Entry is null when nothing was due to accrue.
public record AccrualDto(JournalEntryDto? Entry, decimal Amount, LoanSummaryDto Loan);