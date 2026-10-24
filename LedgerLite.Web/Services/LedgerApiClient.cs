using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerLite.Contracts;

namespace LedgerLite.Web.Services;

public record PostEntryResult(JournalEntryDto Entry, bool WasDuplicate);

/// The only place in the Web project that makes HTTP calls.
/// Every failure surfaces as an ApiException with a message that is safe to display.
public class LedgerApiClient
{
    private readonly HttpClient _http;

    public LedgerApiClient(HttpClient http) => _http = http;

    // ---- Accounts ----

    public async Task<List<AccountDto>> ListAccountsAsync(string? type = null, CancellationToken ct = default) =>
        (await SendAsync<List<AccountDto>>(HttpMethod.Get, Url("api/accounts", ("type", type)), null, ct)).Value;

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default) =>
        (await SendAsync<AccountDto>(HttpMethod.Post, "api/accounts", request, ct)).Value;

    public async Task<AccountLedgerDto> GetLedgerAsync(
        int accountId, DateOnly? from = null, DateOnly? to = null,
        int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        (await SendAsync<AccountLedgerDto>(
            HttpMethod.Get,
            Url($"api/accounts/{accountId}/ledger",
                ("from", D(from)), ("to", D(to)),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            null, ct)).Value;

    public async Task<AccountDto> SetAccountActiveAsync(int accountId, bool active, CancellationToken ct = default) =>
        (await SendAsync<AccountDto>(
            HttpMethod.Post, $"api/accounts/{accountId}/{(active ? "activate" : "deactivate")}", null, ct)).Value;

    // ---- Journal ----

    public async Task<PagedResultDto<JournalEntrySummaryDto>> ListEntriesAsync(
        DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        (await SendAsync<PagedResultDto<JournalEntrySummaryDto>>(
            HttpMethod.Get,
            Url("api/journal-entries",
                ("from", D(from)), ("to", D(to)),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            null, ct)).Value;

    public async Task<JournalEntryDto> GetEntryAsync(int id, CancellationToken ct = default) =>
        (await SendAsync<JournalEntryDto>(HttpMethod.Get, $"api/journal-entries/{id}", null, ct)).Value;

    /// WasDuplicate is true when the idempotency key matched an entry that was already posted (HTTP 200).
    public async Task<PostEntryResult> PostEntryAsync(PostEntryRequest request, CancellationToken ct = default)
    {
        var response = await SendAsync<JournalEntryDto>(HttpMethod.Post, "api/journal-entries", request, ct);
        return new PostEntryResult(response.Value, response.Status == HttpStatusCode.OK);
    }

    public async Task<JournalEntryDto> ReverseEntryAsync(
        int id, ReverseEntryRequest request, CancellationToken ct = default) =>
        (await SendAsync<JournalEntryDto>(HttpMethod.Post, $"api/journal-entries/{id}/reverse", request, ct)).Value;

    public async Task<AccountDto> OpenSavingsAccountAsync(string customerName, CancellationToken ct = default) =>
    (await SendAsync<AccountDto>(
        HttpMethod.Post, "api/savings/accounts", new OpenSavingsAccountRequest(customerName), ct)).Value;

    public async Task<JournalEntryDto> DepositAsync(int accountId, CashMovementRequest request, CancellationToken ct = default) =>
        (await SendAsync<JournalEntryDto>(
            HttpMethod.Post, $"api/savings/accounts/{accountId}/deposit", request, ct)).Value;

    public async Task<JournalEntryDto> WithdrawAsync(int accountId, CashMovementRequest request, CancellationToken ct = default) =>
        (await SendAsync<JournalEntryDto>(
            HttpMethod.Post, $"api/savings/accounts/{accountId}/withdraw", request, ct)).Value;

    // ---- Reports ----

    public async Task<TrialBalanceDto> GetTrialBalanceAsync(DateOnly? asOf = null, CancellationToken ct = default) =>
        (await SendAsync<TrialBalanceDto>(HttpMethod.Get, Url("api/reports/trial-balance", ("asOf", D(asOf))), null, ct))
        .Value;

    // ---- Loans ----

    public async Task<List<LoanSummaryDto>> ListLoansAsync(CancellationToken ct = default) =>
    (await SendAsync<List<LoanSummaryDto>>(HttpMethod.Get, "api/loans", null, ct)).Value;

    public async Task<LoanSummaryDto> GetLoanAsync(int loanId, CancellationToken ct = default) =>
        (await SendAsync<LoanSummaryDto>(HttpMethod.Get, $"api/loans/{loanId}", null, ct)).Value;

    public async Task<LoanSummaryDto> DisburseLoanAsync(DisburseLoanRequest request, CancellationToken ct = default) =>
        (await SendAsync<LoanSummaryDto>(HttpMethod.Post, "api/loans", request, ct)).Value;

    public async Task<RepaymentDto> RepayLoanAsync(int loanId, RepayLoanRequest request, CancellationToken ct = default) =>
        (await SendAsync<RepaymentDto>(HttpMethod.Post, $"api/loans/{loanId}/repayments", request, ct)).Value;

    public async Task<AccrualDto> AccrueInterestAsync(int loanId, DateOnly periodEnd, CancellationToken ct = default) =>
        (await SendAsync<AccrualDto>(
            HttpMethod.Post, $"api/loans/{loanId}/accruals", new AccrueInterestRequest(periodEnd), ct)).Value;

    // ---- Batching ----
    public async Task<MonthEndResultDto> RunMonthEndAccrualAsync(
        DateOnly periodEnd, IReadOnlyList<int>? loanIds = null, CancellationToken ct = default) =>
        (await SendAsync<MonthEndResultDto>(
            HttpMethod.Post, "api/batch/month-end-accrual", new MonthEndRequest(periodEnd, loanIds), ct)).Value;

    // ---- Plumbing ----

    private record ApiResponse<T>(T Value, HttpStatusCode Status);

    private async Task<ApiResponse<T>> SendAsync<T>(
        HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(0, "Could not reach the server. Check your connection and try again.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await ReadErrorAsync(response, ct);

            var value = await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new ApiException((int)response.StatusCode, "The server returned an empty response.");

            return new ApiResponse<T>(value, response.StatusCode);
        }
    }

    /// Understands both error shapes the Api produces:
    /// our ProblemDetails (detail + code) and the framework's validation errors (errors object).
    private static async Task<ApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        string? detail = null, title = null, code = null;
        var errors = new Dictionary<string, string[]>();

        try
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                detail = GetString(root, "detail");
                title = GetString(root, "title");
                code = GetString(root, "code");

                if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in errs.EnumerateObject())
                    {
                        errors[prop.Name] = prop.Value.ValueKind == JsonValueKind.Array
                            ? prop.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                            : Array.Empty<string>();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // The body wasn't JSON (for example an error page from a proxy). Fall back to a generic message.
        }

        var message =
            detail
            ?? (errors.Count > 0 ? string.Join(" ", errors.SelectMany(e => e.Value)) : null)
            ?? status switch
            {
                401 => "You need to sign in.",
                403 => "You don't have permission to do that.",
                404 => "That item was not found.",
                429 => "Too many requests. Please slow down and try again.",
                >= 500 => "The server had a problem. Please try again.",
                _ => title ?? $"The request failed ({status})."
            };

        return new ApiException(status, message, code, errors);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Url(string path, params (string Key, string? Value)[] query)
    {
        var parts = query
            .Where(q => !string.IsNullOrWhiteSpace(q.Value))
            .Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value!)}")
            .ToList();

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private static string? D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
