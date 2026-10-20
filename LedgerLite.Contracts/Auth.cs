namespace LedgerLite.Contracts;

public record CurrentUserDto(bool IsAuthenticated, string? Id, string? Name, string? Provider, string? Role);