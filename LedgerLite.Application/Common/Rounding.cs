namespace LedgerLite.Application.Common;

/// The one place money is rounded. Change the policy here and nowhere else.
public static class Rounding
{
    public const MidpointRounding Policy = MidpointRounding.AwayFromZero;   // 0.005 -> 0.01

    public static decimal ToCents(decimal amount) => Math.Round(amount, 2, Policy);

    /// One month of simple interest. A single division by 1200 (12 months x 100 percent) keeps it exact.
    public static decimal MonthlyInterest(decimal principal, decimal annualRatePercent) =>
        ToCents(principal * annualRatePercent / 1200m);
}