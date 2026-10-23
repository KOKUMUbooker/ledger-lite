namespace LedgerLite.Application.Savings;

/// The two accounts every savings posting rule is built on.
public record SystemAccountCodes(
    string Cash = "1000",
    string CustomerDeposits = "2000",
    string LoanPortfolio = "1200",
    string InterestReceivable = "1210",
    string InterestIncome = "4000");