using Bit.BlazorUI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using LedgerLite.Web.Services;

namespace LedgerLite.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddBitBlazorUIServices();
        builder.Services.AddScoped<LedgerApiClient>();
        builder.Services.AddAuthorizationCore();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<CookieAuthStateProvider>();
        builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthStateProvider>());

        builder.Services.AddScoped(sp =>
        {
            var handler = new UnauthorizedHandler(sp.GetRequiredService<CookieAuthStateProvider>())
            {
                InnerHandler = new HttpClientHandler()
            };
            return new HttpClient(handler) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
        });

        await builder.Build().RunAsync();
    }
}
