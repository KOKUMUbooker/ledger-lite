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
    IReadOnlyList<JournalLineDto> Lines);