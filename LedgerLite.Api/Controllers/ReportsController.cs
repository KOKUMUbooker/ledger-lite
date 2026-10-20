using LedgerLite.Application.Reports;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly GetTrialBalance _trialBalance;

    public ReportsController(GetTrialBalance trialBalance) => _trialBalance = trialBalance;

    // GET /api/reports/trial-balance?asOf=2026-09-30
    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalanceDto>> TrialBalance(
        [FromQuery(Name = "asOf")] DateOnly? asOf, CancellationToken ct) =>
        Ok((await _trialBalance.ExecuteAsync(asOf, ct)).ToDto());
}