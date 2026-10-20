namespace LedgerLite.Api.Auth;

public static class LedgerRoles
{
    public const string Owner = "Owner";
    public const string Visitor = "Visitor";
}

public static class LedgerPolicies
{
    // Who may post or reverse. Today: owners. Later, per-visitor sandboxes change only this.
    public const string CanWrite = "CanWrite";
}
