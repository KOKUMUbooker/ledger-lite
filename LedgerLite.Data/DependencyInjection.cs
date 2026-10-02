using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerLite.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LedgerDbContext>(options => options.UseSqlServer(connectionString));
        return services;
    }
}