using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;

namespace LedgerLite.Domain.Tests;

public class AccountTests
{
    [Theory]
    [InlineData(AccountType.Asset, true)]
    [InlineData(AccountType.Expense, true)]
    [InlineData(AccountType.Liability, false)]
    [InlineData(AccountType.Equity, false)]
    [InlineData(AccountType.Income, false)]
    public void IsDebitNormal_MatchesAccountingRules(AccountType type, bool expected)
    {
        Assert.Equal(expected, type.IsDebitNormal());
    }

    [Theory]
    [InlineData("", "Cash")]
    [InlineData("1000", "  ")]
    public void Create_MissingCodeOrName_Throws(string code, string name)
    {
        Assert.Throws<InvalidAccountException>(() => Account.Create(code, name, AccountType.Asset));
    }

    [Fact]
    public void Create_IsActiveByDefault_AndCanBeDeactivated()
    {
        var cash = Account.Create("1000", "Cash", AccountType.Asset);
        Assert.True(cash.IsActive);

        cash.Deactivate();
        Assert.False(cash.IsActive);
    }

    [Fact]
    public void BalanceFrom_Asset_IsDebitsMinusCredits()
    {
        var cash = Account.Create("1000", "Cash", AccountType.Asset);
        Assert.Equal(3000m, cash.BalanceFrom(totalDebits: 5000m, totalCredits: 2000m));
    }

    [Fact]
    public void BalanceFrom_Liability_IsCreditsMinusDebits()
    {
        var deposits = Account.Create("2000", "Customer Deposits", AccountType.Liability);
        Assert.Equal(3000m, deposits.BalanceFrom(totalDebits: 2000m, totalCredits: 5000m));
    }
}