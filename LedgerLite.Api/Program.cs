using LedgerLite.Api.Auth;
using LedgerLite.Api.ErrorHandling;
using LedgerLite.Application;
using LedgerLite.Data;

namespace LedgerLite.Api;

public class Program
{
    public static void Main(string[] args)
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

        builder.Services.AddLedgerData(builder.Configuration.GetConnectionString("LedgerDb")!);
        builder.Services.AddLedgerApplication();

        builder.Services.AddControllers();

        // Errors: exceptions -> ProblemDetails JSON
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<LedgerExceptionHandler>();
        builder.Services.AddLedgerAuth(builder.Configuration, builder.Environment);

        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        // Serve the Blazor WASM client (_framework files, then wwwroot and static web assets such as Bit's)
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // Unknown /api/... URLs must stay a real 404, not fall through to index.html
        app.Map("/api/{**path}", () => Results.NotFound());

        // Any other URL (e.g. /accounts/5/ledger) belongs to the Blazor router
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}