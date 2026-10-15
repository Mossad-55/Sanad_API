using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.API;

/// <summary>
/// Isolated in-process API host for PORTAL-03 metadata evidence. Production
/// OpenAPI registration and controllers are reused; inherited connection,
/// provider, and storage settings are replaced with dummy values, startup
/// migrations, test-user seeders, and real background/provider work stay
/// disabled (no PostgreSQL connection is ever opened). Metadata GETs need no
/// data fixtures; scenario tests supply deterministic fake dispatch.
/// </summary>
public sealed class PortalApiMetadataHost : WebApplicationFactory<Program>
{
    public const string TestIssuer = "Sanad.PortalTests";

    public const string TestAudience = "Sanad.TestClients";

    public const string TestSigningKey =
        "portal-tests-only-signing-key-not-for-production-12";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] =
                    "Host=localhost;Port=1;Database=sanad_portal_tests;" +
                    "Username=test;Password=test",
                ["Identity:Jwt:Issuer"] = TestIssuer,
                ["Identity:Jwt:Audience"] = TestAudience,
                ["Identity:Jwt:SigningKey"] = TestSigningKey,

                // Metadata and dispatch scenarios never touch a database or
                // the Development seed fixture; keep both disabled so the
                // host cannot migrate, seed, or call providers on startup.
                ["App:TestUserSeed:Enabled"] = "false",
                ["Database:ApplyMigrationsOnStartup"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOptions<JwtOptions>>();

            services.AddSingleton<IOptions<JwtOptions>>(
                Options.Create(new JwtOptions
                {
                    Issuer = TestIssuer,
                    Audience = TestAudience,
                    SigningKey = TestSigningKey
                }));

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var signingKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(TestSigningKey));

                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = TestIssuer;
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidAudience = TestAudience;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                    options.TokenValidationParameters.IssuerSigningKey = signingKey;
                    options.TokenValidationParameters.TryAllIssuerSigningKeys = true;
                });
        });
    }

    public HttpClient CreatePortalClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
}
