using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Ahlena.API.Options;

public static class AhlenaProxyConfiguration
{
    public static void Configure(ForwardedHeadersOptions options)
    {
        // Preserve the framework's loopback-only proxy trust defaults.
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
    }
}
