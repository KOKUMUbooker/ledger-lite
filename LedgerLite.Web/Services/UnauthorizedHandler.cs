using System.Net;

namespace LedgerLite.Web.Services;

// Sits in front of every API call. On a 401 it tells the app the user is signed out, which flips the
// UI to the sign-in screen. The response still flows through unchanged, so LedgerApiClient behaves as before.
public sealed class UnauthorizedHandler : DelegatingHandler
{
    private readonly CookieAuthStateProvider _auth;

    public UnauthorizedHandler(CookieAuthStateProvider auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) _auth.MarkSignedOut();
        return response;
    }
}