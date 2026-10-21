using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly Dictionary<string, string> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google"] = "Google",
        ["github"] = "GitHub"
    };

    // Browser navigates here (a normal link, not fetch). Sends the user to the provider.
    [HttpGet("login/{provider}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.SignIn)]
    public async Task<IActionResult> Login(
        string provider, [FromQuery] string? returnUrl, [FromServices] IAuthenticationSchemeProvider schemes)
    {
        // Unknown provider, or one without credentials configured, is simply not found
        if (!Providers.TryGetValue(provider, out var scheme) || await schemes.GetSchemeAsync(scheme) is null)
            return NotFound();

        return Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, scheme);
    }

    // Always 200. Anonymous callers get IsAuthenticated = false, so the client can ask
    // "who am I?" at startup without treating "not signed in" as an error.
    [HttpGet("me")]
    [AllowAnonymous]
    public ActionResult<CurrentUserDto> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return new CurrentUserDto(false, null, null, null, null);

        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return new CurrentUserDto(
            true,
            id,
            User.FindFirstValue(ClaimTypes.Name),
            id?.Split(':')[0],
            User.FindFirstValue(ClaimTypes.Role));
    }

    // POST, so a third-party page can't sign you out with an image tag or a link
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.SignIn)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    // Local testing without OAuth. Needs BOTH the Development environment and Auth:EnableDevLogin=true,
    // so a mistake in one setting can't open it up in production.
    [HttpGet("dev-login")]
    [AllowAnonymous]
    public async Task<IActionResult> DevLogin(
        [FromQuery] string? role, [FromQuery] string? returnUrl,
        [FromServices] IWebHostEnvironment env, [FromServices] IConfiguration config)
    {
        if (!env.IsDevelopment() || !config.GetValue<bool>("Auth:EnableDevLogin"))
            return NotFound();

        var asVisitor = string.Equals(role, LedgerRoles.Visitor, StringComparison.OrdinalIgnoreCase);
        var chosen = asVisitor ? LedgerRoles.Visitor : LedgerRoles.Owner;

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, $"dev:{chosen.ToLowerInvariant()}"),
                new Claim(ClaimTypes.Name, $"Dev {chosen}"),
                new Claim(ClaimTypes.Role, chosen)
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return string.IsNullOrEmpty(returnUrl)
            ? Ok(new { signedInAs = $"dev:{chosen.ToLowerInvariant()}", role = chosen })
            : LocalRedirect(SafeReturnUrl(returnUrl));
    }

    // Only same-site paths. Blocks "//evil.com" and absolute URLs (open-redirect attacks).
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
