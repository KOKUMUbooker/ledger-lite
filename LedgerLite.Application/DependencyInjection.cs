using LedgerLite.Application.Accounts;
using LedgerLite.Application.Journal;
using LedgerLite.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}