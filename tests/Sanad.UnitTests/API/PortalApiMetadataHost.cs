using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.API;

/// <summary>
/// Isolated in-process API host for PORTAL-03 metadata evidence. Production
/// controllers, OpenAPI registration, and MVC options are reused; everything
/// else is test-safe:
/// every module connection key points at an unreachable dummy target, all
/// migration paths (including the module-specific finance flag) stay disabled,
/// test-user/other fixture seeders stay disabled, production background jobs
/// are removed, storage uses a test-only temporary root, and DataProtection
/// uses an ephemeral provider.
/// Because module registrations capture connection strings eagerly at startup,
/// configuration overrides alone cannot isolate concrete contexts: every
/// module DbContext is therefore re-registered on isolated InMemory stores,
/// so no PostgreSQL connection is possible and no test touches a real
/// persistent directory.
/// </summary>
public sealed class PortalApiMetadataHost : WebApplicationFactory<Program>
{
    public const string TestIssuer = "Sanad.PortalTests";

    public const string TestAudience = "Sanad.TestClients";

    public const string TestSigningKey =
        "portal-tests-only-signing-key-not-for-production-12";

    public const string DummyConnectionString =
        "Host=localhost;Port=1;Database=sanad_portal_tests;" +
        "Username=test;Password=test";

    public static readonly IReadOnlyDictionary<string, string?> TestOverrides =
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:IdentityDatabase"] = DummyConnectionString,
            ["ConnectionStrings:NotificationsDatabase"] = DummyConnectionString,
            ["ConnectionStrings:CmsDatabase"] = DummyConnectionString,
            ["ConnectionStrings:ChatDatabase"] = DummyConnectionString,
            ["ConnectionStrings:FinanceDatabase"] = DummyConnectionString,
            ["ConnectionStrings:FamiliesDatabase"] = DummyConnectionString,
            ["ConnectionStrings:CareHomesDatabase"] = DummyConnectionString,
            ["ConnectionStrings:CaregiversDatabase"] = DummyConnectionString,
            ["ConnectionStrings:CommunityDatabase"] = DummyConnectionString,
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,

            // Metadata and dispatch scenarios never touch a database or
            // the Development seed fixture; keep every migration path and
            // seeder disabled so the host cannot migrate, seed, or call
            // providers on startup.
            ["App:TestUserSeed:Enabled"] = "false",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["FinanceMigrations:ApplyOnStartup"] = "false",

            // Keep file writes inside a test-only temporary root instead of
            // the default binary-relative directory.
            ["Storage:Local:RootPath"] = Path.Combine(
                Path.GetTempPath(), "sanad-portal-tests-storage"),

            // No persistent key ring: test services replace DataProtection
            // with an ephemeral provider below.
            ["DataProtection:KeyRingPath"] = string.Empty
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(TestOverrides);
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove every production background job (Care Homes
            // processors, Chat dispatch/cleanup, email/push outboxes):
            // none may run on this host.
            services.RemoveAll<IHostedService>();

            // Module registrations read connection strings eagerly while the
            // host is built, so re-register every concrete module context on
            // an isolated InMemory store: production code under test can only
            // ever reach these stores.
            UseInMemory<Sanad.Modules.Identity.Infrastructure.Persistence.IdentityDbContext>(services, "portal-identity");
            UseInMemory<Sanad.Modules.Cms.Infrastructure.Persistence.CmsDbContext>(services, "portal-cms");
            UseInMemory<Sanad.Modules.Caregivers.Infrastructure.Persistence.CaregiversDbContext>(services, "portal-caregivers");
            UseInMemory<Sanad.Modules.Families.Infrastructure.Persistence.FamiliesDbContext>(services, "portal-families");
            UseInMemory<Sanad.Modules.CareHomes.Infrastructure.Persistence.CareHomesDbContext>(services, "portal-carehomes");
            UseInMemory<Sanad.Modules.Notifications.Infrastructure.Persistence.NotificationsDbContext>(services, "portal-notifications");
            UseInMemory<Sanad.Modules.Community.Infrastructure.Persistence.CommunityDbContext>(services, "portal-community");
            UseInMemory<Sanad.Modules.Chat.Infrastructure.Persistence.ChatDbContext>(services, "portal-chat");
            UseInMemory<Sanad.Modules.Finance.Infrastructure.FinanceDbContext>(services, "portal-finance");

            services.AddDataProtection().UseEphemeralDataProtectionProvider();

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

    private static void UseInMemory<TContext>(IServiceCollection services, string name)
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        Microsoft.EntityFrameworkCore.DbContextOptions<TContext> options =
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TContext>()
                .UseInMemoryDatabase(name)
                .Options;
        services.RemoveAll<TContext>();
        services.AddScoped(_ => (TContext)Activator.CreateInstance(typeof(TContext), options)!);
    }
}
