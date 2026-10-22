using LedgerLite.Application.Accounts;
using LedgerLite.Application.Exceptions;
using LedgerLite.Domain.Entities;

namespace LedgerLite.Application.Tests;

public class SetAccountActiveTests : LedgerTestBase
{
    public SetAccountActiveTests(LedgerDbFixture fixture) : base(fixture) { }

    private async Task<Account> SetAsync(int accountId, bool active)
    {
        await using var db = Fixture.CreateContext();
        return await new SetAccountActive(db).ExecuteAsync(new SetAccountActiveCommand(accountId, active));
    }

    [Fact]
    public async Task Deactivate_ZeroBalance_Succeeds_AndBlocksNewPostings()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);

        var result = await SetAsync(cash.Id, false);

        Assert.False(result.IsActive);
        await Assert.ThrowsAsync<AccountInactiveException>(() => PostAsync(cash.Id, deposits.Id, 100m));
    }

    [Fact]
    public async Task Deactivate_WithBalance_IsRejected()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await PostAsync(cash.Id, deposits.Id, 100m);

        await Assert.ThrowsAsync<AccountHasBalanceException>(() => SetAsync(cash.Id, false));
    }

    [Fact]
    public async Task Deactivate_AfterBalanceIsReversedToZero_Succeeds()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        var posted = await PostAsync(cash.Id, deposits.Id, 100m);
        await ReverseAsync(posted.Entry.Id);

        var result = await SetAsync(cash.Id, false);

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Deactivate_ParentWithActiveChild_IsRejected()
    {
        var parent = await NewAccountAsync(AccountType.Liability);
        await using (var db = Fixture.CreateContext())
        {
            var code = $"C{Guid.NewGuid():N}"[..14];
            await new CreateAccount(db).ExecuteAsync(
                new CreateAccountCommand(code, "Child", AccountType.Liability, parent.Id));
        }

        await Assert.ThrowsAsync<AccountHasActiveChildrenException>(() => SetAsync(parent.Id, false));
    }

    [Fact]
    public async Task Deactivate_Twice_IsIdempotent()
    {
        var account = await NewAccountAsync(AccountType.Asset);

        await SetAsync(account.Id, false);
        var again = await SetAsync(account.Id, false);

        Assert.False(again.IsActive);
    }

    [Fact]
    public async Task Activate_RestoresPosting()
    {
        var cash = await NewAccountAsync(AccountType.Asset);
        var deposits = await NewAccountAsync(AccountType.Liability);
        await SetAsync(cash.Id, false);

        await SetAsync(cash.Id, true);
        var result = await PostAsync(cash.Id, deposits.Id, 50m);

        Assert.False(result.WasDuplicate);
    }

    [Fact]
    public async Task Activate_ChildOfInactiveParent_IsRejected()
    {
        var parent = await NewAccountAsync(AccountType.Liability);
        Account child;
        await using (var db = Fixture.CreateContext())
        {
            var code = $"C{Guid.NewGuid():N}"[..14];
            child = await new CreateAccount(db).ExecuteAsync(
                new CreateAccountCommand(code, "Child", AccountType.Liability, parent.Id));
        }

        await SetAsync(child.Id, false);
        await SetAsync(parent.Id, false);

        await Assert.ThrowsAsync<InvalidParentAccountException>(() => SetAsync(child.Id, true));
    }

    [Fact]
    public async Task UnknownAccount_IsNotFound()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(() => SetAsync(int.MaxValue, false));
    }
}