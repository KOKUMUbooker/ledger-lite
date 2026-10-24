using LedgerLite.Application.Batch;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Loans;
using LedgerLite.Application.Reports;
using LedgerLite.Contracts;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Api;

internal static class Mapping
{
    private static string NormalBalanceOf(AccountType type) => type.IsDebitNormal() ? "Debit" : "Credit";

    // SQL Server returns Kind=Unspecified; mark it UTC so JSON gets a "Z" and clients don't treat it as local time
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static AccountDto ToDto(this Account a) =>
        new(a.Id, a.Code, a.Name, a.Type.ToString(), NormalBalanceOf(a.Type), a.ParentAccountId, a.IsActive);

    public static JournalEntryDto ToDto(this JournalEntry e, int? reversedByEntryId = null) => new(
        e.Id,
        e.Date,
        AsUtc(e.PostedAt),
        e.Description,
        e.Reference,
        e.Source.ToString(),
        e.IdempotencyKey,
        e.ReversalOfEntryId,
        reversedByEntryId,
        e.Lines.OrderBy(l => l.Id)
            .Select(l => new JournalLineDto(l.Id, l.AccountId, l.Debit, l.Credit))
            .ToList());

    public static JournalEntryDto ToDto(this JournalEntryView view) =>
        view.Entry.ToDto(view.ReversedByEntryId);

    public static JournalEntrySummaryDto ToDto(this JournalEntrySummary s) => new(
        s.Id, s.Date, AsUtc(s.PostedAt), s.Description, s.Reference, s.Source.ToString(),
        s.ReversalOfEntryId, s.ReversedByEntryId, s.Total, s.LineCount);

    public static AccountLedgerDto ToDto(this AccountLedger l) => new(
        l.AccountId, l.Code, l.Name, l.Type.ToString(), NormalBalanceOf(l.Type),
        l.From, l.To,
        l.OpeningBalance,
        l.Page, l.PageSize, l.TotalLines,
        l.BroughtForward,
        l.Lines.Select(x => new LedgerLineDto(
            x.Date, x.EntryId, x.Description, x.Reference, x.Debit, x.Credit, x.RunningBalance)).ToList(),
        l.TotalDebits, l.TotalCredits,
        l.ClosingBalance);

    public static TrialBalanceDto ToDto(this TrialBalance t) => new(
        t.AsOf,
        t.Rows.Select(r => new TrialBalanceRowDto(
            r.AccountId, r.Code, r.Name, r.Type.ToString(), r.TotalDebits, r.TotalCredits, r.Balance , r.HasChildren, r.RolledUpBalance )).ToList(),
        t.GrandTotalDebits,
        t.GrandTotalCredits,
        t.IsBalanced);

    public static LoanDto ToDto(this Loan l) => new(
        l.Id, l.CustomerName, l.PrincipalAmount, l.AnnualRatePercent, l.TermMonths,
        l.DisbursedOn, l.PrincipalAccountId, l.InterestAccountId);

    public static LoanSummaryDto ToDto(this LoanSummary s) => new(
        s.Loan.ToDto(), s.PrincipalOutstanding, s.InterestOutstanding, s.TotalOutstanding, s.IsSettled);

    public static MonthEndResultDto ToDto(this MonthEndAccrualResult r) => new(
        r.PeriodEnd,
        r.Count(AccrualOutcome.Accrued), r.Count(AccrualOutcome.AlreadyAccrued),
        r.Count(AccrualOutcome.NothingToAccrue), r.Count(AccrualOutcome.Failed),
        r.TotalInterest,
        r.Loans.Select(l => new LoanAccrualResultDto(
            l.LoanId, l.CustomerName, l.Outcome.ToString(), l.Amount, l.Error)).ToList());
}