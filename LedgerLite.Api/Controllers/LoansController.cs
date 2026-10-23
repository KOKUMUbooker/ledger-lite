using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application.Loans;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Route("api/loans")]
[Authorize]
[EnableRateLimiting(RateLimitingExtensions.Reads)]
public class LoansController : ControllerBase
{
    private readonly ListLoans _list;
    private readonly GetLoanSummary _get;
    private readonly DisburseLoan _disburse;
    private readonly AccrueInterest _accrue;
    private readonly RepayLoan _repay;

    public LoansController(ListLoans list, GetLoanSummary get, DisburseLoan disburse,
        AccrueInterest accrue, RepayLoan repay)
    {
        _list = list; _get = get; _disburse = disburse; _accrue = accrue; _repay = repay;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // GET /api/loans
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LoanSummaryDto>>> List(CancellationToken ct) =>
        Ok((await _list.ExecuteAsync(ct)).Select(s => s.ToDto()).ToList());

    // GET /api/loans/3
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LoanSummaryDto>> Get(int id, CancellationToken ct) =>
        Ok((await _get.ExecuteAsync(id, ct)).ToDto());

    // POST /api/loans   (201 new, 200 replay)
    [HttpPost]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<LoanSummaryDto>> Disburse(DisburseLoanRequest r, CancellationToken ct)
    {
        var result = await _disburse.ExecuteAsync(new DisburseLoanCommand(
            r.CustomerName, r.Principal, r.AnnualRatePercent, r.TermMonths, r.Date ?? Today, r.IdempotencyKey), ct);

        var summary = (await _get.ExecuteAsync(result.Loan.Id, ct)).ToDto();
        return result.WasDuplicate ? Ok(summary) : StatusCode(201, summary);
    }

    // POST /api/loans/3/repayments
    [HttpPost("{id:int}/repayments")]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<RepaymentDto>> Repay(int id, RepayLoanRequest r, CancellationToken ct)
    {
        var result = await _repay.ExecuteAsync(
            new RepayLoanCommand(id, r.Amount, r.Date ?? Today, r.Narration, r.IdempotencyKey), ct);

        var dto = new RepaymentDto(
            result.Entry.ToDto(), result.InterestPaid, result.PrincipalPaid,
            (await _get.ExecuteAsync(id, ct)).ToDto());

        return result.WasDuplicate ? Ok(dto) : StatusCode(201, dto);
    }

    // POST /api/loans/3/accruals   { "periodEnd": "2026-10-31" }
    [HttpPost("{id:int}/accruals")]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<AccrualDto>> Accrue(int id, AccrueInterestRequest r, CancellationToken ct)
    {
        var result = await _accrue.ExecuteAsync(new AccrueInterestCommand(id, r.PeriodEnd), ct);
        var summary = (await _get.ExecuteAsync(id, ct)).ToDto();

        if (result is null) return Ok(new AccrualDto(null, 0m, summary));

        var amount = result.Entry.Lines.Where(l => l.AccountId == summary.Loan.InterestAccountId).Sum(l => l.Debit);
        var dto = new AccrualDto(result.Entry.ToDto(), amount, summary);
        return result.WasDuplicate ? Ok(dto) : StatusCode(201, dto);
    }
}