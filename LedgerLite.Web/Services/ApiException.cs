namespace LedgerLite.Web.Services;

/// Any failed API call. Message is safe to show to the user.
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool IsUnauthorized => StatusCode == 401;

    public ApiException(
        int statusCode, string message, string? code = null,
        IReadOnlyDictionary<string, string[]>? errors = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}