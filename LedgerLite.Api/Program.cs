using LedgerLite.Api.Auth;
using LedgerLite.Api.ErrorHandling;
using LedgerLite.Api.Maintenance;
using LedgerLite.Api.Proxy;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application;
using LedgerLite.Data;

namespace LedgerLite.Api;

public class Program
{
    //public static void Main(string[] args)
    public static async Task<int> Main(string[] args)
    {
        // Load .env, searching upward from the working directory, BEFORE the builder reads configuration.
        // NoClobber: variables already set in the real environment (on the server, say) win over the file.
        try
        {
            DotNetEnv.Env.NoClobber().TraversePath().Load();
        }
        catch (FileNotFoundException)
        {
            // No .env file is fine: production can use real environment variables instead.
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();

        // Errors: exceptions -> ProblemDetails JSON
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<LedgerExceptionHandler>();

        builder.Services.AddLedgerData(builder.Configuration.GetConnectionString("LedgerDb")!);
        builder.Services.AddLedgerApplication();
        builder.Services.AddLedgerAuth(builder.Configuration, builder.Environment);
        builder.Services.AddLedgerForwardedHeaders(builder.Configuration);
        builder.Services.AddLedgerRateLimiting(builder.Configuration);
        builder.Services.AddHealthChecks();

        // Guard against oversized request bodies
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.UseForwardedHeaders();      // FIRST: everything after it sees the real client IP and scheme

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        // Serve the Blazor WASM client (_framework files, then wwwroot and static web assets such as Bit's)
        app.MapStaticAssets().ShortCircuit(); // Also short circuit requests for file requests

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseRateLimiter();           // after authentication so it can key by user

        app.MapControllers();

        app.MapHealthChecks("/healthz");

        // Unknown /api/... URLs must stay a real 404, not fall through to index.html
        app.Map("/api/{**path}", () => Results.NotFound());

        // Any other URL (e.g. /accounts/5/ledger) belongs to the Blazor router
        app.MapFallbackToFile("index.html");

        // reset-db / seed-demo run and exit instead of starting the web server
        var exitCode = await MaintenanceCommands.TryRunAsync(app, args);
        if (exitCode is not null) return exitCode.Value;

        await app.RunAsync();

        return 0;
    }
}