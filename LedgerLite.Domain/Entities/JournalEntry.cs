using LedgerLite.Domain.Exceptions;

namespace LedgerLite.Domain.Entities;

public class JournalEntry
{
    private readonly List<JournalLine> _lines = new();

    private JournalEntry() { } // for EF Core

    public int Id { get; private set; }
    public DateOnly Date { get; private set; }          // accounting date
    public DateTime PostedAt { get; private set; }      // when it was recorded (UTC)
    public string Description { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public EntrySource Source { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? ReversalOfEntryId { get; private set; }
    public IReadOnlyList<JournalLine> Lines => _lines;

    public static JournalEntry Create(
        DateOnly date, string description, string reference, IEnumerable<JournalLine> lines,
        EntrySource source = EntrySource.Manual, string? idempotencyKey = null)
    {
        if (source == EntrySource.Reversal)
            throw new InvalidJournalEntryException("Reversals can only be created with CreateReversal.");

        return Build(date, description, reference, lines, source, idempotencyKey, reversalOfEntryId: null);
    }

    /// Builds a NEW entry that cancels this one. The original is never modified.
    public JournalEntry CreateReversal(DateOnly date, string? description = null)
    {
        if (Id == 0)
            throw new InvalidJournalEntryException("Only a saved entry can be reversed.");

        return Build(
            date,
            description ?? $"Reversal of #{Id}: {Description}",
            $"REV-{Reference}",
            _lines.Select(l => l.Flipped()),
            EntrySource.Reversal,
            idempotencyKey: null,
            reversalOfEntryId: Id);
    }

    private static JournalEntry Build(
        DateOnly date, string description, string reference, IEnumerable<JournalLine> lines,
        EntrySource source, string? idempotencyKey, int? reversalOfEntryId)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidJournalEntryException("Description is required.");

        var lineList = lines.ToList();

        if (lineList.Count < 2)
            throw new InvalidJournalEntryException("An entry needs at least two lines.");

        foreach (var line in lineList)
            ValidateLine(line);

        var totalDebits = lineList.Sum(l => l.Debit);
        var totalCredits = lineList.Sum(l => l.Credit);

        if (totalDebits != totalCredits)
            throw new UnbalancedEntryException(totalDebits, totalCredits);

        var entry = new JournalEntry
        {
            Date = date,
            PostedAt = DateTime.UtcNow,
            Description = description.Trim(),
            Reference = reference?.Trim() ?? string.Empty,
            Source = source,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            ReversalOfEntryId = reversalOfEntryId
        };
        entry._lines.AddRange(lineList);
        return entry;
    }

    private static void ValidateLine(JournalLine line)
    {
        if (line.Debit < 0 || line.Credit < 0)
            throw new InvalidJournalEntryException("Amounts cannot be negative.");

        if (line.Debit == 0 && line.Credit == 0)
            throw new InvalidJournalEntryException("A line must have a debit or a credit amount.");

        if (line.Debit != 0 && line.Credit != 0)
            throw new InvalidJournalEntryException("A line cannot have both a debit and a credit.");

        if (Math.Round(line.Debit, 2) != line.Debit || Math.Round(line.Credit, 2) != line.Credit)
            throw new InvalidJournalEntryException("Amounts cannot have more than 2 decimal places.");
    }
}