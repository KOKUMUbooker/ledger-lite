namespace LedgerLite.Domain.Entities;

public enum EntrySource
{
    Manual,
    Reversal
    DepositCash,
    WithdrawCash
    // Stage 2 adds: InterestAccrual, LoanDisbursement, LoanRepayment
}