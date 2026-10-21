namespace LedgerLite.Api.RateLimiting;

public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int ApiPerMinute { get; set; } = 300;        // per-IP floor for all API calls
    public int ReadsPerMinute { get; set; } = 120;      // per user (per IP if anonymous)
    public int WritesPerMinute { get; set; } = 20;      // per user (per IP if anonymous)
    public int SignInPerMinute { get; set; } = 10;      // attempts per window, per IP
    public int SignInWindowMinutes { get; set; } = 1;   // set 15 for "10 per 15 minutes" 
}
