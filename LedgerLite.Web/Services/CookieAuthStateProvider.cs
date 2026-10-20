using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace LedgerLite.Web.Services;

// Asks the Api "who am I?" and turns the answer into a ClaimsPrincipal. The login cookie itself is
// HttpOnly, so the browser sends it automatically and this code never sees it.
public sealed class CookieAuthStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    // Its own HttpClient on purpose: the main one depends on this class (401 handling), and sharing
    // would be a circular dependency.
    private readonly HttpClient _http;
    private Task<AuthenticationState>? _state;

    public CookieAuthStateProvider(NavigationManager nav) =>
        _http = new HttpClient { BaseAddress = new Uri(nav.BaseUri) };

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var me = await _http.GetFromJsonAsync<CurrentUserDto>("api/auth/me");
            if (me is { IsAuthenticated: true })
            {
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, me.Id ?? string.Empty),
                        new Claim(ClaimTypes.Name, me.Name ?? string.Empty),
                        new Claim(ClaimTypes.Role, me.Role ?? string.Empty)
                    },
                    authenticationType: "cookie",
                    nameType: ClaimTypes.Name,
                    roleType: ClaimTypes.Role);

                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Api unreachable or an unexpected reply: treat as signed out
        }

        return Anonymous;
    }

    // Called when any API call comes back 401 (cookie expired or removed)
    public void MarkSignedOut()
    {
        _state = Task.FromResult(Anonymous);
        NotifyAuthenticationStateChanged(_state);
    }

    public async Task SignOutAsync()
    {
        try { await _http.PostAsync("api/auth/logout", content: null); }
        catch (HttpRequestException) { /* nothing more we can do */ }
        MarkSignedOut();
    }
}