using LedgerLite.Application.Accounts;
using LedgerLite.Application.Batch;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Loans;
using LedgerLite.Application.Reports;
using LedgerLite.Application.Savings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LedgerLite.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateAccount>();
        services.AddScoped<PostJournalEntry>();
        services.AddScoped<ReverseJournalEntry>();
        services.AddScoped<GetAccountLedger>();
        services.AddScoped<GetTrialBalance>();
        services.AddScoped<ListAccounts>();
        services.AddScoped<GetJournalEntry>();
        services.AddScoped<ListJournalEntries>();
        services.AddScoped<SetAccountActive>();

        services.AddSingleton(new SystemAccountCodes());   // defaults: Cash 1000, Customer Deposits 2000
        services.AddScoped<OpenSavingsAccount>();
        services.AddScoped<DepositCash>();
        services.AddScoped<WithdrawCash>();

        services.AddScoped<DisburseLoan>();
        services.AddScoped<AccrueInterest>();
        services.AddScoped<RepayLoan>();
        services.AddScoped<GetLoanSummary>();
        services.AddScoped<ListLoans>();

        services.AddScoped<RunMonthEndAccrual>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}