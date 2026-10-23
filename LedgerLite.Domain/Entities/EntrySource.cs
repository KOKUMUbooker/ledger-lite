namespace LedgerLite.Domain.Entities;

public enum EntrySource
{
    Manual,
    Reversal,
    DepositCash,
    WithdrawCash,
    InterestAccrual, 
    LoanDisbursement, 
    LoanRepayment
}