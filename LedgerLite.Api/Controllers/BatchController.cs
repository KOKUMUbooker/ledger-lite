using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application.Batch;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Route("api/batch")]
[Authorize(Policy = LedgerPolicies.CanWrite)]
[EnableRateLimiting(RateLimitingExtensions.Writes)]
public class BatchController : ControllerBase
{
    private readonly RunMonthEndAccrual _run;

    public BatchController(RunMonthEndAccrual run) => _run = run;

    // POST /api/batch/month-end-accrual   { "periodEnd": "2026-10-31", "loanIds": [1, 2] }
    // Always 200 when the run happened, with a result per loan (some may say Failed).
    [HttpPost("month-end-accrual")]
    public async Task<ActionResult<MonthEndResultDto>> MonthEndAccrual(MonthEndRequest request, CancellationToken ct) =>
        Ok((await _run.ExecuteAsync(new MonthEndAccrualCommand(request.PeriodEnd, request.LoanIds), ct)).ToDto());
}