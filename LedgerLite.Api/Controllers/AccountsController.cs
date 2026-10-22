using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application.Accounts;
using LedgerLite.Application.Reports;
using LedgerLite.Contracts;
using LedgerLite.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts")]
[EnableRateLimiting(RateLimitingExtensions.Reads)]
public class AccountsController : ControllerBase
{
    private readonly ListAccounts _listAccounts;
    private readonly CreateAccount _createAccount;
    private readonly GetAccountLedger _getLedger;
    private readonly SetAccountActive _setActive;

    public AccountsController(
        ListAccounts listAccounts, CreateAccount createAccount,
        GetAccountLedger getLedger, SetAccountActive setActive)
    {
        _listAccounts = listAccounts;
        _createAccount = createAccount;
        _getLedger = getLedger;
        _setActive = setActive;
    }

    // GET /api/accounts?type=Asset
    [HttpGet]
    public async Task<ActionResult<List<AccountDto>>> List([FromQuery] string? type, CancellationToken ct)
    {
        AccountType? filter = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!TryParseType(type, out var parsed)) return InvalidType(type);
            filter = parsed;
        }

        var accounts = await _listAccounts.ExecuteAsync(filter, ct);
        return Ok(accounts.Select(a => a.ToDto()).ToList());
    }

    // POST /api/accounts
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountRequest request, CancellationToken ct)
    {
        if (!TryParseType(request.Type, out var type)) return InvalidType(request.Type);

        var account = await _createAccount.ExecuteAsync(
            new CreateAccountCommand(request.Code, request.Name, type, request.ParentAccountId), ct);

        return Created($"/api/accounts/{account.Id}/ledger", account.ToDto());
    }

    // GET /api/accounts/5/ledger?from=2026-09-01&to=2026-09-30&page=2&pageSize=50
    [HttpGet("{id:int}/ledger")]
    public async Task<ActionResult<AccountLedgerDto>> Ledger(
        int id,
        [FromQuery(Name = "from")] DateOnly? fromDate,
        [FromQuery(Name = "to")] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,   // same default as the use case; out-of-range values are clamped there
        CancellationToken ct = default)
    {
        if (fromDate > toDate)
            return Problem("'from' must not be after 'to'.", statusCode: 400, title: "Invalid request");

        var ledger = await _getLedger.ExecuteAsync(id, fromDate, toDate, page, pageSize, ct);
        return Ok(ledger.ToDto());
    }

    // POST /api/accounts/5/deactivate
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<AccountDto>> Deactivate(int id, CancellationToken ct) =>
        Ok((await _setActive.ExecuteAsync(new SetAccountActiveCommand(id, false), ct)).ToDto());

    // POST /api/accounts/5/activate
    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<AccountDto>> Activate(int id, CancellationToken ct) =>
        Ok((await _setActive.ExecuteAsync(new SetAccountActiveCommand(id, true), ct)).ToDto());

    private static bool TryParseType(string? value, out AccountType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);

    private ActionResult InvalidType(string? value) =>
        Problem(
            $"'{value}' is not a valid account type. Use Asset, Liability, Equity, Income or Expense.",
            statusCode: 400, title: "Invalid request");
}