namespace LedgerLite.Contracts;

public record PostLineRequest(int AccountId, decimal Debit, decimal Credit);

public record PostEntryRequest(
    DateOnly Date,
    string Description,
    string Reference,
    List<PostLineRequest> Lines,
    string? IdempotencyKey = null);

public record ReverseEntryRequest(DateOnly Date, string? Description = null);

public record JournalLineDto(int Id, int AccountId, decimal Debit, decimal Credit);

public record JournalEntryDto(
    int Id,
    DateOnly Date,
    DateTime PostedAt,
    string Description,
    string Reference,
    string Source,
    string? IdempotencyKey,
    int? ReversalOfEntryId,
    int? ReversedByEntryId,
    IReadOnlyList<JournalLineDto> Lines);

/// One row in the journal list: no lines, just a total and a line count.
public record JournalEntrySummaryDto(
    int Id,
    DateOnly Date,
    DateTime PostedAt,
    string Description,
    string Reference,
    string Source,
    int? ReversalOfEntryId,
    int? ReversedByEntryId,
    decimal Total,
    int LineCount);

public record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);