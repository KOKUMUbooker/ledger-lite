using LedgerLite.Application.Journal;
using LedgerLite.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LedgerLite.Api.Controllers;

[ApiController]
[Route("api/journal-entries")]
public class JournalEntriesController : ControllerBase
{
    private readonly PostJournalEntry _post;
    private readonly ReverseJournalEntry _reverse;
    private readonly GetJournalEntry _get;

    public JournalEntriesController(PostJournalEntry post, ReverseJournalEntry reverse, GetJournalEntry get)
    {
        _post = post;
        _reverse = reverse;
        _get = get;
    }

    // POST /api/journal-entries
    // 201 = newly posted, 200 = the idempotency key matched an entry that was already posted
    [HttpPost]
    public async Task<ActionResult<JournalEntryDto>> Post(PostEntryRequest request, CancellationToken ct)
    {
        if (request.Date == default) return MissingDate();

        var lines = (request.Lines ?? new())
            .Select(l => new PostLine(l.AccountId, l.Debit, l.Credit))
            .ToList();

        var result = await _post.ExecuteAsync(new PostJournalEntryCommand(
            request.Date, request.Description, request.Reference, lines, request.IdempotencyKey), ct);

        var dto = result.Entry.ToDto();
        return result.WasDuplicate
            ? Ok(dto)
            : Created($"/api/journal-entries/{dto.Id}", dto);
    }

    // GET /api/journal-entries/42
    [HttpGet("{id:int}")]
    public async Task<ActionResult<JournalEntryDto>> GetById(int id, CancellationToken ct) =>
        Ok((await _get.ExecuteAsync(id, ct)).ToDto());

    // POST /api/journal-entries/42/reverse
    [HttpPost("{id:int}/reverse")]
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
}