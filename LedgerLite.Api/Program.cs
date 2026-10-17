using LedgerLite.Api.ErrorHandling;
using LedgerLite.Application;
using LedgerLite.Data;

namespace LedgerLite.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddLedgerData(builder.Configuration.GetConnectionString("LedgerDb")!);
        builder.Services.AddLedgerApplication();

        builder.Services.AddControllers();

        // Errors: exceptions -> ProblemDetails JSON
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<LedgerExceptionHandler>();

        // Blazor WASM runs on a different origin, so the browser needs CORS permission
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

        builder.Services.AddOpenApi();   // keep the template's own OpenAPI registration if it differs

        var app = builder.Build();

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();            // keep the template's own OpenAPI mapping if it differs
        }

        app.UseHttpsRedirection();
        app.UseCors();

        app.MapControllers();

        app.Run();
    }
}