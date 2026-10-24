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
[Authorize]
[EnableRateLimiting(RateLimitingExtensions.Reads)]
public class BatchController : ControllerBase
{
    private readonly RunMonthEndAccrual _run;
    private readonly PreviewMonthEndAccrual _preview;

    public BatchController(RunMonthEndAccrual run, PreviewMonthEndAccrual preview)
    {
        _run = run;
        _preview = preview;
    }

    // POST /api/batch/month-end-accrual   { "periodEnd": "2026-10-31", "loanIds": [1, 2] }
    // Always 200 when the run happened, with a result per loan (some may say Failed).
    [HttpPost("month-end-accrual")]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<MonthEndResultDto>> MonthEndAccrual(MonthEndRequest request, CancellationToken ct) =>
        Ok((await _run.ExecuteAsync(new MonthEndAccrualCommand(request.PeriodEnd, request.LoanIds), ct)).ToDto());

    // POST /api/batch/month-end-accrual/preview   same body; posts nothing, so any signed-in user may call it
    [HttpPost("month-end-accrual/preview")]
    public async Task<ActionResult<MonthEndPreviewDto>> Preview(MonthEndRequest request, CancellationToken ct) =>
        Ok((await _preview.ExecuteAsync(new MonthEndAccrualCommand(request.PeriodEnd, request.LoanIds), ct)).ToDto());
}
