using LedgerLite.Application.Accounts;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Reports;
using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerLite.Application.Tests;

public class PreviewTests : LedgerTestBase
{
    public PreviewTests(LedgerDbFixture fixture) : base(fixture) { }

    private static readonly DateOnly Date = new(2026, 10, 20);

    private static PostJournalEntryCommand Cmd(string? key = null, params PostLine[] lines) =>
        new(Date, "Preview test", $"PRV-{Guid.NewGuid():N}"[..16], lines, key);

    private async Task<JournalEntryPreview> PreviewAsync(PostJournalEntryCommand c)
    {
        await using var db = Fixture.CreateContext();
        return await new PreviewJournalEntry(db).ExecuteAsync(c);
    }

    private async Task<PostJournalEntryResult> PostCommandAsync(PostJournalEntryCommand c)
    {
        await using var db = Fixture.CreateContext();
        return await new PostJournalEntry(db).ExecuteAsync(c);
    }

    private async Task<decimal> BalanceOfAsync(int accountId)
    {
        await using var db = Fixture.CreateContext();
        return (await new GetAccountLedger(db).ExecuteAsync(accountId)).ClosingBalance;
    }

    [Fact]
    public async Task Preview_SavesNothing()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var cmd = Cmd(null, new PostLine(cash.Id, 100m, 0m), new PostLine(deposits.Id, 0m, 100m));

        await PreviewAsync(cmd);

        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.JournalEntries.CountAsync(e => e.Reference == cmd.Reference));
        Assert.Equal(0, await db.JournalLines.CountAsync(l => l.AccountId == cash.Id || l.AccountId == deposits.Id));
    }

    [Fact]
    public async Task Preview_ShowsBalanceBeforeAndAfter()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await PostAsync(cash.Id, deposits.Id, 1_000m);

        var preview = await PreviewAsync(Cmd(null, new PostLine(cash.Id, 250m, 0m), new PostLine(deposits.Id, 0m, 250m)));

        var cashLine = preview.Lines.Single(l => l.AccountId == cash.Id);
        Assert.Equal(1_000m, cashLine.BalanceBefore);
        Assert.Equal(1_250m, cashLine.BalanceAfter);

        var depositLine = preview.Lines.Single(l => l.AccountId == deposits.Id);
        Assert.Equal(1_000m, depositLine.BalanceBefore);
        Assert.Equal(1_250m, depositLine.BalanceAfter);

        Assert.Equal(250m, preview.TotalDebits);
        Assert.Equal(250m, preview.TotalCredits);
        Assert.Empty(preview.Warnings);
        Assert.Null(preview.WouldReplayEntryId);
    }

    [Fact]
    public async Task PreviewedBalances_MatchTheRealOnesAfterPosting()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await PostAsync(cash.Id, deposits.Id, 400m);
        var cmd = Cmd(null, new PostLine(deposits.Id, 150m, 0m), new PostLine(cash.Id, 0m, 150m));

        var preview = await PreviewAsync(cmd);
        await PostCommandAsync(cmd);

        foreach (var line in preview.Lines)
            Assert.Equal(line.BalanceAfter, await BalanceOfAsync(line.AccountId));
    }

    [Fact]
    public async Task Preview_WarnsWhenAnAccountWouldGoBelowZero()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        // Nothing in either account: paying out 100 pushes both negative
        var preview = await PreviewAsync(Cmd(null, new PostLine(deposits.Id, 100m, 0m), new PostLine(cash.Id, 0m, 100m)));

        Assert.Equal(2, preview.Warnings.Count);
        Assert.Contains(preview.Warnings, w => w.Contains(cash.Code));
        Assert.Contains(preview.Warnings, w => w.Contains(deposits.Code));
    }

    [Fact]
    public async Task Preview_WithUsedKey_SaysPostingWouldReplay()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var key = $"prv-{Guid.NewGuid():N}";
        var cmd = Cmd(key, new PostLine(cash.Id, 100m, 0m), new PostLine(deposits.Id, 0m, 100m));
        var posted = await PostCommandAsync(cmd);

        var preview = await PreviewAsync(cmd);

        Assert.Equal(posted.Entry.Id, preview.WouldReplayEntryId);
        Assert.Empty(preview.Lines);
    }

    [Fact]
    public async Task PreviewAndPost_RejectTheSameBadInput_WithTheSameError()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var closed = await NewAccountAsync(AccountType.Asset);
        await using (var db = Fixture.CreateContext())
            await new SetAccountActive(db).ExecuteAsync(new SetAccountActiveCommand(closed.Id, false));

        var cases = new Dictionary<string, PostJournalEntryCommand>
        {
            ["unbalanced"] = Cmd(null, new PostLine(cash.Id, 100m, 0m), new PostLine(deposits.Id, 0m, 90m)),
            ["both sides on one line"] = Cmd(null, new PostLine(cash.Id, 100m, 100m), new PostLine(deposits.Id, 0m, 100m)),
            ["unknown account"] = Cmd(null, new PostLine(cash.Id, 100m, 0m), new PostLine(int.MaxValue, 0m, 100m)),
            ["inactive account"] = Cmd(null, new PostLine(closed.Id, 100m, 0m), new PostLine(deposits.Id, 0m, 100m)),
            ["single line"] = Cmd(null, new PostLine(cash.Id, 100m, 0m)),
            ["zero amounts"] = Cmd(null, new PostLine(cash.Id, 0m, 0m), new PostLine(deposits.Id, 0m, 0m)),
            ["three decimals"] = Cmd(null, new PostLine(cash.Id, 100.005m, 0m), new PostLine(deposits.Id, 0m, 100.005m)),
        };

        foreach (var (name, cmd) in cases)
        {
            var previewError = await CaptureAsync(() => PreviewAsync(cmd));
            var postError = await CaptureAsync(() => PostCommandAsync(cmd));

            Assert.True(postError is not null, $"'{name}' should be rejected by posting");
            Assert.True(previewError is not null, $"'{name}' should be rejected by preview");
            Assert.Equal(postError!.GetType(), previewError!.GetType());
            Assert.Equal(postError.Message, previewError.Message);
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}