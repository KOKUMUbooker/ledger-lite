using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace LedgerLite.Api.Proxy;

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddLedgerForwardedHeaders(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;   // exactly one trusted hop: our own reverse proxy

            // By default only a proxy on this machine (loopback) is trusted, which is what you want when
            // nginx runs on the same VPS as the app. Extra trusted addresses (a Docker network gateway, say)
            // go in Proxy__KnownProxies__0, __1, ...
            foreach (var value in config.GetSection("Proxy:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
                if (IPAddress.TryParse(value, out var ip)) o.KnownProxies.Add(ip);
        });

        return services;
    }
}
