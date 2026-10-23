using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application.Savings;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Route("api/savings")]
[Authorize(Policy = LedgerPolicies.CanWrite)]
[EnableRateLimiting(RateLimitingExtensions.Writes)]
public class SavingsController : ControllerBase
{
    private readonly OpenSavingsAccount _open;
    private readonly DepositCash _deposit;
    private readonly WithdrawCash _withdraw;

    public SavingsController(OpenSavingsAccount open, DepositCash deposit, WithdrawCash withdraw)
    {
        _open = open;
        _deposit = deposit;
        _withdraw = withdraw;
    }



    // POST /api/savings/accounts   { "customerName": "Oliver Twist" }
    [HttpPost("accounts")]
    public async Task<ActionResult<AccountDto>> Open(OpenSavingsAccountRequest request, CancellationToken ct)
    {
        var account = await _open.ExecuteAsync(new OpenSavingsAccountCommand(request.CustomerName), ct);
        return CreatedAtAction(nameof(Open), new { id = account.Id }, account.ToDto());
    }

    [HttpPost("accounts/{id:int}/deposit")]
    public async Task<ActionResult<JournalEntryDto>> Deposit(
        int id, CashMovementRequest request, CancellationToken ct)
    {
        var result = await _deposit.ExecuteAsync(ToCommand(id, request), ct);
        return result.WasDuplicate ? Ok(result.Entry.ToDto()) : StatusCode(201, result.Entry.ToDto());
    }

    [HttpPost("accounts/{id:int}/withdraw")]
    public async Task<ActionResult<JournalEntryDto>> Withdraw(
        int id, CashMovementRequest request, CancellationToken ct)
    {
        var result = await _withdraw.ExecuteAsync(ToCommand(id, request), ct);
        return result.WasDuplicate ? Ok(result.Entry.ToDto()) : StatusCode(201, result.Entry.ToDto());
    }

    private static CashMovementCommand ToCommand(int id, CashMovementRequest r) =>
        new(id, r.Amount, r.Date ?? DateOnly.FromDateTime(DateTime.UtcNow), r.Narration, r.IdempotencyKey);
}