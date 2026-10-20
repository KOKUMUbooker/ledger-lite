using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace LedgerLite.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddLedgerAuth(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Owners are provider-prefixed stable ids, e.g. "github:1234567", "google:1098765...".
        // Ids, not emails: emails can change or be recycled, ids can't.
        var owners = new HashSet<string>(
            config.GetSection("Auth:Owners").Get<string[]>() ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var auth = services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = "ledgerlite.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // http://localhost works in development, anywhere else the cookie is https-only
                o.Cookie.SecurePolicy = env.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;

                // This is an API plus an SPA: answer with a status code, never a redirect to a login page
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        var google = config.GetSection("Authentication:Google");
        if (Has(google))
        {
            auth.AddGoogle(o =>
            {
                o.ClientId = google["ClientId"]!;
                o.ClientSecret = google["ClientSecret"]!;
                o.Scope.Clear();            // no email: we only need an opaque id and a display name
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Events.OnTicketReceived = ctx => Rebuild(ctx, owners);
            });
        }

        var github = config.GetSection("Authentication:GitHub");
        if (Has(github))
        {
            auth.AddGitHub(o =>
            {
                o.ClientId = github["ClientId"]!;
                o.ClientSecret = github["ClientSecret"]!;
                o.Events.OnTicketReceived = ctx => Rebuild(ctx, owners);
            });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(LedgerPolicies.CanWrite, p => p.RequireRole(LedgerRoles.Owner));

        return services;
    }

    private static bool Has(IConfigurationSection s) =>
        !string.IsNullOrWhiteSpace(s["ClientId"]) && !string.IsNullOrWhiteSpace(s["ClientSecret"]);

    // Replaces whatever the provider sent with a small identity of our own:
    // a provider-prefixed id, a display name and a role. Nothing else is kept.
    private static Task Rebuild(TicketReceivedContext ctx, HashSet<string> owners)
    {
        var externalId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(externalId))
        {
            ctx.Fail("The sign-in provider did not return a user id.");
            return Task.CompletedTask;
        }

        var id = $"{ctx.Scheme.Name.ToLowerInvariant()}:{externalId}";   // "google:..." or "github:..."
        var name = ctx.Principal!.FindFirstValue(ClaimTypes.Name) ?? "Signed-in user";
        if (name.Length > 100) name = name[..100];

        var role = owners.Contains(id) ? LedgerRoles.Owner : LedgerRoles.Visitor;

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Role, role)
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        ctx.Principal = new ClaimsPrincipal(identity);
        return Task.CompletedTask;
    }
}