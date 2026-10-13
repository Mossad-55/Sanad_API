using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Sanad.API.Options;

public static class SanadProxyConfiguration
{
    public static void Configure(ForwardedHeadersOptions options)
    {
        // Preserve the framework's loopback-only proxy trust defaults.
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
    }
}
