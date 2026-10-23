namespace LedgerLite.Domain.Entities;

/// Loan terms. Balances are never stored: they come from the two ledger accounts below.
public class Loan
{
    private Loan() { }   // EF

    public int Id { get; private set; }
    public string CustomerName { get; private set; } = "";
    public decimal PrincipalAmount { get; private set; }
    public decimal AnnualRatePercent { get; private set; }
    public int TermMonths { get; private set; }
    public DateOnly DisbursedOn { get; private set; }
    public int PrincipalAccountId { get; private set; }
    public int InterestAccountId { get; private set; }

    /// Makes a retried disbursement return the original loan instead of creating a second one.
    public string? DisbursementKey { get; private set; }

    public static Loan Create(
        string customerName, decimal principal, decimal annualRatePercent, int termMonths,
        DateOnly disbursedOn, int principalAccountId, int interestAccountId, string? disbursementKey) =>
        new()
        {
            CustomerName = customerName,
            PrincipalAmount = principal,
            AnnualRatePercent = annualRatePercent,
            TermMonths = termMonths,
            DisbursedOn = disbursedOn,
            PrincipalAccountId = principalAccountId,
            InterestAccountId = interestAccountId,
            DisbursementKey = disbursementKey
        };
}