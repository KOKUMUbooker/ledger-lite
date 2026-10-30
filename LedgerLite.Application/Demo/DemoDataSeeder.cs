using LedgerLite.Application.Batch;
using LedgerLite.Application.Common;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Loans;
using LedgerLite.Application.Reports;
using LedgerLite.Application.Savings;
using LedgerLite.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerLite.Application.Demo;

public class DemoDataAlreadyPresentException : Exception
{
    public DemoDataAlreadyPresentException()
        : base("The database already contains entries or loans. Run reset-db first.") { }
}

public record DemoSeedSummary(int Customers, int Accounts, int Entries, int Loans, bool IsBalanced, decimal TotalDebits);

/// Fills an empty database with a believable five-month history, using the real use cases.
public class DemoDataSeeder
{
    private static readonly string[] Customers =
    {
        "Jane Wanjiru", "Peter Otieno", "Amina Hassan", "Brian Kiplagat",
        "Grace Achieng", "Samuel Mwangi", "Fatuma Ali", "David Kamau"
    };

    private static readonly decimal[] OpeningDeposits = { 25_000m, 80_000m, 12_000m, 45_000m, 150_000m, 9_000m, 33_000m, 60_000m };
    private static readonly int[] OpeningDays = { 3, 4, 6, 8, 9, 10, 11, 12 };

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly SystemAccountCodes _codes;

    public DemoDataSeeder(IServiceScopeFactory scopes, TimeProvider clock, SystemAccountCodes codes)
    {
        _scopes = scopes;
        _clock = clock;
        _codes = codes;
    }

    private sealed record Step(DateOnly Date, int Order, Func<Task> Run);

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task<DemoSeedSummary> SeedAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        // Month 0 is five months ago, month 5 is the current (partial) month
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        DateOnly D(int month, int day)
        {
            var m = start.AddMonths(month);
            return new DateOnly(m.Year, m.Month, Math.Min(day, DateTime.DaysInMonth(m.Year, m.Month)));
        }
        DateOnly EndOfMonth(int month) => D(month, 31);

        await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LedgerDbContext>();
            if (await db.JournalEntries.AnyAsync(ct) || await db.Loans.AnyAsync(ct))
                throw new DemoDataAlreadyPresentException();
            return 0;
        });

        // The accounts every story needs
        var (cashId, capitalId, expenseId) = await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LedgerDbContext>();
            var cash = await SavingsRules.RequireByCodeAsync(db, _codes.Cash, ct);
            var capital = await SavingsRules.RequireByCodeAsync(db, "3000", ct);    // Share Capital
            var expense = await SavingsRules.RequireByCodeAsync(db, "5000", ct);    // Interest Expense
            return (cash.Id, capital.Id, expense.Id);
        });

        // One savings account per customer
        var savings = new int[Customers.Length];
        for (var i = 0; i < Customers.Length; i++)
        {
            var name = Customers[i];
            savings[i] = (await InScopeAsync(sp =>
                sp.GetRequiredService<OpenSavingsAccount>().ExecuteAsync(new OpenSavingsAccountCommand(name), ct))).Id;
        }

        // ---- Build the timeline; steps run in date order, so every balance check sees the right state ----
        var steps = new List<Step>();
        void Add(DateOnly date, int order, Func<Task> run)
        {
            if (date <= today) steps.Add(new Step(date, order, run));   // never in the future
        }

        void Deposit(int c, int month, int day, decimal amount, string key) =>
            Add(D(month, day), 1, async () => await InScopeAsync(sp =>
                sp.GetRequiredService<DepositCash>().ExecuteAsync(
                    new CashMovementCommand(savings[c], amount, D(month, day), null, key), ct)));

        void Withdraw(int c, int month, int day, decimal amount) =>
            Add(D(month, day), 2, async () => await InScopeAsync(sp =>
                sp.GetRequiredService<WithdrawCash>().ExecuteAsync(
                    new CashMovementCommand(savings[c], amount, D(month, day), null, $"demo-wdr-{c}-{month}-{day}"), ct)));

        // 0. Share capital paid in: Dr Cash, Cr Share Capital
        Add(D(0, 1), 0, async () => await InScopeAsync(sp =>
            sp.GetRequiredService<PostJournalEntry>().ExecuteAsync(new PostJournalEntryCommand(
                D(0, 1), "Share capital paid in", "SHARECAP-0001",
                new[] { new PostLine(cashId, 600_000m, 0m), new PostLine(capitalId, 0m, 600_000m) },
                "demo-share-capital"), ct)));

        // 1. Customers open their accounts
        for (var c = 0; c < Customers.Length; c++)
            Deposit(c, 0, OpeningDays[c], OpeningDeposits[c], $"demo-open-{c}");

        // 2. Regular savings and some withdrawals (customer, month, day, amount)
        foreach (var (c, m, d, a) in new (int, int, int, decimal)[]
        {
            (0, 1, 5, 5_000m), (0, 2, 5, 5_000m), (0, 3, 5, 5_000m), (0, 4, 5, 5_000m), (0, 5, 5, 5_000m),
            (1, 2, 20, 40_000m), (2, 1, 25, 3_000m), (2, 3, 25, 3_000m), (3, 4, 22, 15_000m),
            (4, 3, 14, 75_000m), (5, 4, 7, 4_500m), (6, 2, 9, 20_000m), (7, 2, 18, 30_000m)
        })
            Deposit(c, m, d, a, $"demo-dep-{c}-{m}-{d}");

        foreach (var (c, m, d, a) in new (int, int, int, decimal)[]
        {
            (0, 4, 28, 8_000m), (1, 3, 10, 25_000m), (2, 2, 11, 4_000m),
            (4, 4, 16, 60_000m), (6, 3, 15, 10_000m), (7, 4, 5, 12_000m)
        })
            Withdraw(c, m, d, a);

        // 3. A keying mistake, corrected the way a ledger corrects: reverse, then post the right amount
        int? mistakeEntryId = null;
        Add(D(1, 14), 1, async () =>
        {
            var posted = await InScopeAsync(sp => sp.GetRequiredService<DepositCash>().ExecuteAsync(
                new CashMovementCommand(savings[6], 90_000m, D(1, 14), "Cash deposit (keyed in error)", "demo-fatuma-typo"), ct));
            mistakeEntryId = posted.Entry.Id;
        });
        Add(D(1, 15), 2, async () =>
        {
            if (mistakeEntryId is null) return;
            await InScopeAsync(sp => sp.GetRequiredService<ReverseJournalEntry>().ExecuteAsync(
                new ReverseJournalEntryCommand(mistakeEntryId.Value, D(1, 15), "Reversal: 90,000 keyed instead of 9,000"), ct));
        });
        Deposit(6, 1, 15, 9_000m, "demo-fatuma-correct");

        // 4. Interest credited to a few savers: Dr Interest Expense, Cr the customer's savings
        foreach (var (c, m, a) in new (int, int, decimal)[] { (4, 2, 450m), (4, 4, 900m), (1, 3, 300m) })
        {
            var eom = EndOfMonth(m);
            Add(eom, 5, async () => await InScopeAsync(sp => sp.GetRequiredService<PostJournalEntry>().ExecuteAsync(
                new PostJournalEntryCommand(
                    eom, $"Savings interest credited · {Customers[c]}", $"SINT-{c}-{m}",
                    new[] { new PostLine(expenseId, a, 0m), new PostLine(savings[c], 0m, a) },
                    $"demo-int-{c}-{m}"), ct)));
        }

        // 5. Four loans
        var loanIds = new Dictionary<string, int>();

        void Disburse(string key, string customer, decimal principal, decimal rate, int term, int month, int day) =>
            Add(D(month, day), 3, async () =>
            {
                var result = await InScopeAsync(sp => sp.GetRequiredService<DisburseLoan>().ExecuteAsync(
                    new DisburseLoanCommand(customer, principal, rate, term, D(month, day), $"demo-loan-{key}"), ct));
                loanIds[key] = result.Loan.Id;
            });

        // A regular instalment (principal / term) plus the interest owed; settle = clear everything
        void Repay(string key, int month, int day, bool settle = false) =>
            Add(D(month, day), 4, async () =>
            {
                if (!loanIds.TryGetValue(key, out var id)) return;

                var summary = await InScopeAsync(sp => sp.GetRequiredService<GetLoanSummary>().ExecuteAsync(id, ct));
                if (summary.IsSettled) return;

                var instalment = Rounding.ToCents(summary.Loan.PrincipalAmount / summary.Loan.TermMonths);
                var amount = settle
                    ? summary.TotalOutstanding
                    : Math.Min(summary.InterestOutstanding + instalment, summary.TotalOutstanding);
                if (amount <= 0m) return;

                await InScopeAsync(sp => sp.GetRequiredService<RepayLoan>().ExecuteAsync(
                    new RepayLoanCommand(id, amount, D(month, day), null, $"demo-rpy-{key}-{month}"), ct));
            });

        Disburse("a", "Samuel Mwangi", 50_000m, 14m, 12, 1, 8);        // pays regularly
        for (var m = 2; m <= 5; m++) Repay("a", m, 15);

        Disburse("b", "Brian Kiplagat", 120_000m, 12m, 24, 1, 20);     // pays regularly
        for (var m = 2; m <= 5; m++) Repay("b", m, 20);

        Disburse("c", "Amina Hassan", 30_000m, 18m, 6, 2, 4);          // repaid early, so settled
        Repay("c", 3, 4);
        Repay("c", 4, 12, settle: true);

        Disburse("d", "David Kamau", 200_000m, 10m, 36, 3, 10);        // pays once, then stops
        Repay("d", 4, 10);

        // 6. Month-end: the real batch, for every completed month
        for (var m = 0; m <= 5; m++)
        {
            var eom = EndOfMonth(m);
            Add(eom, 9, async () =>
            {
                if (loanIds.Count == 0) return;

                var result = await InScopeAsync(sp => sp.GetRequiredService<RunMonthEndAccrual>().ExecuteAsync(
                    new MonthEndAccrualCommand(eom, loanIds.Values.ToList()), ct));

                if (result.Count(AccrualOutcome.Failed) > 0)
                    throw new InvalidOperationException($"Month-end accrual failed for {eom:yyyy-MM}.");
            });
        }

        // ---- Run it, oldest first ----
        foreach (var step in steps.OrderBy(s => s.Date).ThenBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();
            await step.Run();
        }

        return await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LedgerDbContext>();
            var trial = await new GetTrialBalance(db).ExecuteAsync(null, ct);

            return new DemoSeedSummary(
                Customers.Length,
                await db.Accounts.CountAsync(ct),
                await db.JournalEntries.CountAsync(ct),
                await db.Loans.CountAsync(ct),
                trial.GrandTotalDebits == trial.GrandTotalCredits,
                trial.GrandTotalDebits);
        });
    }
}