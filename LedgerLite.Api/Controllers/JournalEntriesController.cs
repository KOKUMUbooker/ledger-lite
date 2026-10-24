using LedgerLite.Api.Auth;
using LedgerLite.Api.RateLimiting;
using LedgerLite.Application.Journal;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/journal-entries")]
[EnableRateLimiting(RateLimitingExtensions.Reads)]
public class JournalEntriesController : ControllerBase
{
    private readonly PostJournalEntry _post;
    private readonly ReverseJournalEntry _reverse;
    private readonly GetJournalEntry _get;
    private readonly ListJournalEntries _list;
    private readonly PreviewJournalEntry _preview;

    public JournalEntriesController(
        PostJournalEntry post, ReverseJournalEntry reverse, GetJournalEntry get, ListJournalEntries list, PreviewJournalEntry preview)
    {
        _post = post;
        _reverse = reverse;
        _get = get;
        _list = list;
        _preview = preview;
    }

    // GET /api/journal-entries?from=2026-09-01&to=2026-09-30&page=1&pageSize=20   (newest first)
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<JournalEntrySummaryDto>>> List(
        [FromQuery(Name = "from")] DateOnly? fromDate,
        [FromQuery(Name = "to")] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (fromDate > toDate)
            return Problem("'from' must not be after 'to'.", statusCode: 400, title: "Invalid request");

        var result = await _list.ExecuteAsync(fromDate, toDate, page, pageSize, ct);

        return Ok(new PagedResultDto<JournalEntrySummaryDto>(
            result.Items.Select(i => i.ToDto()).ToList(), result.Page, result.PageSize, result.TotalCount));
    }

    // POST /api/journal-entries
    // 201 = newly posted, 200 = the idempotency key matched an entry that was already posted
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    [Authorize(Policy = LedgerPolicies.CanWrite)]
    public async Task<ActionResult<JournalEntryDto>> Post(PostEntryRequest request, CancellationToken ct)
    {
        if (request.Date == default) return MissingDate();

        var result = await _post.ExecuteAsync(ToCommand(request), ct);

        if (!result.WasDuplicate)
        {
            var created = result.Entry.ToDto();
            return Created($"/api/journal-entries/{created.Id}", created);
        }

        // A replay may refer to an entry that has been reversed since, so return its current state
        var current = await _get.ExecuteAsync(result.Entry.Id, ct);
        return Ok(current.ToDto());
    }

    // POST /api/journal-entries/preview
    // Same body as posting; saves nothing. Not under CanWrite: previewing changes nothing, so any signed-in user may try it.
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitingExtensions.Reads)]
    public async Task<ActionResult<JournalEntryPreviewDto>> Preview(PostEntryRequest request, CancellationToken ct)
    {
        if (request.Date == default) return MissingDate();

        return Ok((await _preview.ExecuteAsync(ToCommand(request), ct)).ToDto());
    }

    // GET /api/journal-entries/42
    [HttpGet("{id:int}")]
    public async Task<ActionResult<JournalEntryDto>> GetById(int id, CancellationToken ct) =>
        Ok((await _get.ExecuteAsync(id, ct)).ToDto());

    // POST /api/journal-entries/42/reverse
    [HttpPost("{id:int}/reverse")]
    [EnableRateLimiting(RateLimitingExtensions.Writes)]
    public async Task<ActionResult<JournalEntryDto>> Reverse(int id, ReverseEntryRequest request, CancellationToken ct)
    {
        if (request.Date == default) return MissingDate();

        var reversal = await _reverse.ExecuteAsync(
            new ReverseJournalEntryCommand(id, request.Date, request.Description), ct);

        var dto = reversal.ToDto();
        return Created($"/api/journal-entries/{dto.Id}", dto);
    }

    private ActionResult MissingDate() =>
        Problem("A date is required (format: yyyy-MM-dd).", statusCode: 400, title: "Invalid request");

    private static PostJournalEntryCommand ToCommand(PostEntryRequest request) => new(
        request.Date, request.Description, request.Reference,
        (request.Lines ?? new()).Select(l => new PostLine(l.AccountId, l.Debit, l.Credit)).ToList(),
        request.IdempotencyKey);
}