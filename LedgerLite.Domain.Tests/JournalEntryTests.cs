using LedgerLite.Domain.Entities;
using LedgerLite.Domain.Exceptions;

namespace LedgerLite.Domain.Tests;

public class JournalEntryTests
{
    private const int Cash = 1;
    private const int CustomerDeposits = 2;
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static JournalEntry ValidDeposit(decimal amount = 5000m) =>
        JournalEntry.Create(Today, "Cash deposit", "DEP-001", new[]
        {
            JournalLine.ForDebit(Cash, amount),
            JournalLine.ForCredit(CustomerDeposits, amount)
        });

    private static void SetId(JournalEntry entry, int id) =>
        typeof(JournalEntry).GetProperty(nameof(JournalEntry.Id))!.SetValue(entry, id);

    [Fact]
    public void Create_BalancedEntry_Succeeds()
    {
        var entry = ValidDeposit();

        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Create_UnbalancedEntry_Throws()
    {
        var ex = Assert.Throws<UnbalancedEntryException>(() =>
            JournalEntry.Create(Today, "Bad", "X", new[]
            {
                JournalLine.ForDebit(Cash, 5000m),
                JournalLine.ForCredit(CustomerDeposits, 4999m)
            }));

        Assert.Equal(5000m, ex.TotalDebits);
        Assert.Equal(4999m, ex.TotalCredits);
    }

    [Fact]
    public void Create_SingleLine_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() =>
            JournalEntry.Create(Today, "One line", "X", new[] { JournalLine.ForDebit(Cash, 100m) }));
    }

    [Fact]
    public void Create_NegativeAmount_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() =>
            JournalEntry.Create(Today, "Negative", "X", new[]
            {
                JournalLine.ForDebit(Cash, -100m),
                JournalLine.ForCredit(CustomerDeposits, -100m)
            }));
    }

    [Fact]
    public void Create_ZeroAmountLine_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() =>
            JournalEntry.Create(Today, "Zero", "X", new[]
            {
                JournalLine.ForDebit(Cash, 0m),
                JournalLine.ForCredit(CustomerDeposits, 0m)
            }));
    }

    [Fact]
    public void Create_TooManyDecimals_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() => ValidDeposit(100.005m));
    }

    [Fact]
    public void Create_MissingDescription_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() =>
            JournalEntry.Create(Today, "  ", "X", new[]
            {
                JournalLine.ForDebit(Cash, 100m),
                JournalLine.ForCredit(CustomerDeposits, 100m)
            }));
    }

    [Fact]
    public void Create_MultiLineBalancedEntry_Succeeds()
    {
        // Loan repayment style: one debit, two credits
        var entry = JournalEntry.Create(Today, "Repayment", "LN-1", new[]
        {
            JournalLine.ForDebit(Cash, 1100m),
            JournalLine.ForCredit(3, 1000m),  // loan receivable (principal)
            JournalLine.ForCredit(4, 100m)    // interest income
        });

        Assert.Equal(3, entry.Lines.Count);
    }

    [Fact]
    public void CreateReversal_FlipsDebitsAndCredits()
    {
        var original = ValidDeposit();
        SetId(original, 4471);

        var reversal = original.CreateReversal(Today);

        var cashLine = reversal.Lines.Single(l => l.AccountId == Cash);
        var depositLine = reversal.Lines.Single(l => l.AccountId == CustomerDeposits);
        Assert.Equal(5000m, cashLine.Credit);
        Assert.Equal(0m, cashLine.Debit);
        Assert.Equal(5000m, depositLine.Debit);
        Assert.Equal(0m, depositLine.Credit);
    }

    [Fact]
    public void CreateReversal_LinksToOriginal_AndLeavesOriginalUntouched()
    {
        var original = ValidDeposit();
        SetId(original, 4471);

        var reversal = original.CreateReversal(Today);

        Assert.Equal(4471, reversal.ReversalOfEntryId);
        Assert.Null(original.ReversalOfEntryId);
        Assert.Equal(5000m, original.Lines.Single(l => l.AccountId == Cash).Debit);
    }

    [Fact]
    public void CreateReversal_NetEffectIsZero()
    {
        var original = ValidDeposit();
        SetId(original, 4471);
        var reversal = original.CreateReversal(Today);

        var all = original.Lines.Concat(reversal.Lines).ToList();

        Assert.Equal(all.Sum(l => l.Debit), all.Sum(l => l.Credit));
        foreach (var accountId in new[] { Cash, CustomerDeposits })
        {
            var lines = all.Where(l => l.AccountId == accountId);
            Assert.Equal(0m, lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit));
        }
    }

    [Fact]
    public void CreateReversal_OfUnsavedEntry_Throws()
    {
        var unsaved = ValidDeposit(); // Id == 0
        Assert.Throws<InvalidJournalEntryException>(() => unsaved.CreateReversal(Today));
    }

    [Fact]
    public void Create_DefaultsToManualSource()
    {
        Assert.Equal(EntrySource.Manual, ValidDeposit().Source);
    }

    [Fact]
    public void Create_WithReversalSource_Throws()
    {
        Assert.Throws<InvalidJournalEntryException>(() =>
            JournalEntry.Create(Today, "Sneaky", "X", new[]
            {
            JournalLine.ForDebit(Cash, 100m),
            JournalLine.ForCredit(CustomerDeposits, 100m)
            }, source: EntrySource.Reversal));
    }

    [Fact]
    public void Create_StoresTrimmedIdempotencyKey_AndBlankBecomesNull()
    {
        var lines = new[] { JournalLine.ForDebit(Cash, 100m), JournalLine.ForCredit(CustomerDeposits, 100m) };

        var keyed = JournalEntry.Create(Today, "Keyed", "X", lines, idempotencyKey: "  accrual:1:2026-09-30 ");
        var blank = JournalEntry.Create(Today, "Blank", "X", lines, idempotencyKey: "  ");

        Assert.Equal("accrual:1:2026-09-30", keyed.IdempotencyKey);
        Assert.Null(blank.IdempotencyKey);
    }

    [Fact]
    public void CreateReversal_SetsReversalSource()
    {
        var original = ValidDeposit();
        SetId(original, 4471);

        Assert.Equal(EntrySource.Reversal, original.CreateReversal(Today).Source);
    }
}