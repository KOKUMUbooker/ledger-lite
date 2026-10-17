using LedgerLite.Application.Reports;
using LedgerLite.Contracts;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Api;

internal static class Mapping
{
    public static AccountDto ToDto(this Account a) =>
        new(a.Id, a.Code, a.Name, a.Type.ToString(), a.ParentAccountId, a.IsActive);

    public static JournalEntryDto ToDto(this JournalEntry e) => new(
        e.Id,
        e.Date,
        // SQL Server returns Kind=Unspecified; mark it UTC so JSON gets a "Z" and clients don't treat it as local time
        DateTime.SpecifyKind(e.PostedAt, DateTimeKind.Utc),
        e.Description,
        e.Reference,
        e.Source.ToString(),
        e.IdempotencyKey,
        e.ReversalOfEntryId,
        e.Lines.OrderBy(l => l.Id)
            .Select(l => new JournalLineDto(l.Id, l.AccountId, l.Debit, l.Credit))
            .ToList());

    public static AccountLedgerDto ToDto(this AccountLedger l) => new(
        l.AccountId, l.Code, l.Name, l.Type.ToString(),
        l.From, l.To,
        l.OpeningBalance,
        l.Lines.Select(x => new LedgerLineDto(
            x.Date, x.EntryId, x.Description, x.Reference, x.Debit, x.Credit, x.RunningBalance)).ToList(),
        l.TotalDebits, l.TotalCredits,
        l.ClosingBalance);

    public static TrialBalanceDto ToDto(this TrialBalance t) => new(
        t.AsOf,
        t.Rows.Select(r => new TrialBalanceRowDto(
            r.AccountId, r.Code, r.Name, r.Type.ToString(), r.TotalDebits, r.TotalCredits, r.Balance)).ToList(),
        t.GrandTotalDebits,
        t.GrandTotalCredits,
        t.IsBalanced);
}