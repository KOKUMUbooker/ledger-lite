using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.RateLimiting;

public static class RateLimitingExtensions
{
    // Policy names: use these constants in [EnableRateLimiting(...)]
    public const string SignIn = "signin";
    public const string Writes = "writes";
    public const string Reads = "reads";

    public static IServiceCollection AddLedgerRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new RateLimitSettings();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Same error shape as the rest of the Api: ProblemDetails with a "code"
            options.OnRejected = async (context, ct) =>
            {
                var extensions = new Dictionary<string, object?> { ["code"] = "RateLimited" };

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                    extensions["retryAfterSeconds"] = seconds;
                }

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        detail: "Too many requests. Please wait a moment and try again.",
                        extensions: extensions)
                    .ExecuteAsync(context.HttpContext);
            };

            // ── Global floor: per client IP, API calls only ───────────────────────
            // The IP layer catches someone spreading requests across many accounts.
            // Static files and Blazor routes are skipped (Cloudflare handles those).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                if (!settings.Enabled) return RateLimitPartition.GetNoLimiter("disabled");

                var path = ctx.Request.Path;
                var ip = ClientIp(ctx);

                // The OAuth callbacks are middleware, not controller actions, so no attribute can cover them
                if (path.Value?.StartsWith("/signin-", StringComparison.OrdinalIgnoreCase) ?? false)
                    return Sliding($"callback:{ip}", settings.SignInPerMinute, TimeSpan.FromMinutes(settings.SignInWindowMinutes));

                if (!path.StartsWithSegments("/api")) return RateLimitPartition.GetNoLimiter("not-api");

                return Sliding($"api:{ip}", settings.ApiPerMinute, TimeSpan.FromMinutes(1));
            });

            // ── Sign-in: per IP, tightest ─────────────────────────────────────────
            options.AddPolicy(SignIn, ctx => settings.Enabled
                ? Sliding($"signin:{ClientIp(ctx)}", settings.SignInPerMinute, TimeSpan.FromMinutes(settings.SignInWindowMinutes))
                : RateLimitPartition.GetNoLimiter("disabled"));

            // ── Writes and reads: per signed-in user, per IP when anonymous ───────
            options.AddPolicy(Writes, ctx => settings.Enabled
                ? Sliding(KeyFor(ctx, "write"), settings.WritesPerMinute, TimeSpan.FromMinutes(1))
                : RateLimitPartition.GetNoLimiter("disabled"));

            options.AddPolicy(Reads, ctx => settings.Enabled
                ? Sliding(KeyFor(ctx, "read"), settings.ReadsPerMinute, TimeSpan.FromMinutes(1))
                : RateLimitPartition.GetNoLimiter("disabled"));
        });

        return services;
    }

    private static RateLimitPartition<string> Sliding(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            SegmentsPerWindow = 6,   // the window slides in steps, so no double burst at a boundary
            QueueLimit = 0,          // reject immediately; never queue a ledger write
            AutoReplenishment = true
        });

    // Needs UseAuthentication() to have run first, otherwise everyone looks anonymous
    private static string KeyFor(HttpContext ctx, string bucket)
    {
        var userId = ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        return userId is not null ? $"{bucket}:user:{userId}" : $"{bucket}:ip:{ClientIp(ctx)}";
    }

    // Relies on UseForwardedHeaders() so this is the real client, not the proxy.
    // IPv6 clients usually own a whole /64, so group by that instead of single addresses.
    private static string ClientIp(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        return ip.AddressFamily == AddressFamily.InterNetworkV6
            ? Convert.ToHexString(ip.GetAddressBytes(), 0, 8) + "/64"
            : ip.ToString();
    }
}