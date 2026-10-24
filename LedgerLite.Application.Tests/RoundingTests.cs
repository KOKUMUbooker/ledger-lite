using LedgerLite.Application.Common;

namespace LedgerLite.Application.Tests;

public class RoundingTests
{
    [Theory]
    [InlineData(1_000, 10, "8.33")]      // 8.3333...
    [InlineData(10_000, 12, "100.00")]
    [InlineData(5_000, 12, "50.00")]
    [InlineData(0, 12, "0.00")]
    [InlineData(1_000, 0, "0.00")]
    public void MonthlyInterest_RoundsToCents(int principal, int rate, string expected) =>
        Assert.Equal(decimal.Parse(expected), Rounding.MonthlyInterest(principal, rate));

    [Fact]
    public void ExactMidpoint_RoundsAwayFromZero_NotToEven()
    {
        // 6 x 1% / 12 = 0.005 exactly. Banker's rounding would give 0.00; our policy gives 0.01.
        Assert.Equal(0.01m, Rounding.MonthlyInterest(6m, 1m));
        Assert.Equal(0.02m, Rounding.MonthlyInterest(18m, 1m));   // 0.015 -> 0.02 either way
    }

    [Theory]
    [InlineData("2.675", "2.68")]
    [InlineData("2.665", "2.67")]
    [InlineData("-2.675", "-2.68")]
    public void ToCents_UsesTheSamePolicyForEverything(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected), Rounding.ToCents(decimal.Parse(input)));
}