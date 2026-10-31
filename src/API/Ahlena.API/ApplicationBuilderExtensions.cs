namespace Ahlena.API;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Ahlena.API.Seeding;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Persistence.Seeding;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;
using Ahlena.Modules.Community.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Infrastructure;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseAhlenaApi(
        this WebApplication app)
    {
        TestUserSeedTargetGuard.EnsureSafeTarget(
            app.Configuration,
            app.Environment);

        app.UseForwardedHeaders();

        app.UseExceptionHandler();

        app.UseStatusCodePages();

        UseLocalFiles(app);

        app.UseHttpsRedirection();

        app.UseAuthentication();

        app.UseCors();

        app.UseAuthorization();

        app.MapControllers();

        // Chat Plus realtime hub. Requires the reverse proxy to forward
        // WebSocket upgrades with TLS (see the operating guide); REST
        // remains authoritative and sync is the fallback.
        app.MapHub<Hubs.ChatHub>("/hubs/chat", options =>
            options.CloseOnAuthenticationExpiration = true);

        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/openapi/v1.json",
                    "Ahlena API v1");
            });
        }

        // Startup migration switch, evaluated once. The L1 fixture workflow
        // disables startup migrations, so this must never reach an
        // ApplyMigrations call in that mode.
        bool applyMigrations = StartupMigrationPolicy.ShouldApplyModuleMigrations(
            app.Configuration,
            app.Environment);

        if (applyMigrations)
        {
            ApplyMigrations<IdentityDbContext>(app);
            ApplyMigrations<CmsDbContext>(app);
            ApplyMigrations<CaregiversDbContext>(app);
            ApplyMigrations<FamiliesDbContext>(app);
            ApplyMigrations<CareHomesDbContext>(app);
            ApplyMigrations<NotificationsDbContext>(app);
            ApplyMigrations<CommunityDbContext>(app);
            ApplyMigrations<ChatDbContext>(app);
            if (StartupMigrationPolicy.ShouldApplyFinanceMigrations(
                    app.Configuration,
                    app.Environment))
            {
                ApplyMigrations<FinanceDbContext>(app);
            }
        }

        // Fixture seeding is decoupled from the migration switch: the
        // persistent-local fixture workflow runs the API with startup
        // migrations explicitly disabled (never invoked above) and still
        // needs the existing SuperAdmin/FinanceAdmin/TestUser seeding. Every
        // other host keeps the original behavior: seed only together with
        // migrations. The seed target guard itself refuses non-Development
        // or non-local targets before anything is written.
        if (applyMigrations
            || TestUserSeedTargetGuard.IsPersistentLocalFixtureRun(app.Configuration))
        {
            SeedSuperAdmin(app);
            SeedFinanceAdmin(app);
            SeedTestUsers(app);
        }

        return app;
    }

    private static void ApplyMigrations<TContext>(WebApplication app)
        where TContext : DbContext
    {
        string contextName = typeof(TContext).Name;
        ILogger logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Ahlena.DatabaseMigrations");

        try
        {
            using IServiceScope scope = app.Services.CreateScope();
            TContext dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            string[] pending = dbContext.Database.GetPendingMigrations().ToArray();
            logger.LogInformation(
                "Database migration check started for {DbContext}; {PendingCount} pending migration(s).",
                contextName,
                pending.Length);

            dbContext.Database.Migrate();

            logger.LogInformation(
                "Database migration check completed for {DbContext}; {AppliedCount} migration(s) applied.",
                contextName,
                pending.Length);
        }
        catch
        {
            logger.LogError(
                "Database migration failed for {DbContext}; API startup will stop.",
                contextName);
            throw;
        }
    }

    private static void SeedSuperAdmin(
        WebApplication app)
    {
        using IServiceScope scope =
            app.Services.CreateScope();

        SuperAdminSeeder seeder =
            scope.ServiceProvider.GetRequiredService<
                SuperAdminSeeder>();

        seeder.SeedAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static void SeedFinanceAdmin(
        WebApplication app)
    {
        using IServiceScope scope =
            app.Services.CreateScope();

        FinanceAdminSeeder seeder =
            scope.ServiceProvider.GetRequiredService<
                FinanceAdminSeeder>();

        seeder.SeedAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static void SeedTestUsers(
        WebApplication app)
    {
        using IServiceScope scope =
            app.Services.CreateScope();

        // Minimal/test hosts may not register the optional seeder — skip there.
        TestUserDataSeeder? seeder =
            scope.ServiceProvider.GetService<
                TestUserDataSeeder>();

        if (seeder is null)
        {
            return;
        }

        seeder.SeedAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static void UseLocalFiles(
        WebApplication app)
    {
        LocalStorageOptions storageOptions =
            app.Services
                .GetRequiredService<
                    IOptions<LocalStorageOptions>>()
                .Value;

        string root =
            storageOptions.GetEffectiveRootPath();

        // Release folders are replaced on deploy and restored on rollback:
        // outside Development uploads must live in a durable directory
        // outside them. Deploy/rollback procedures must never delete or
        // move shared files.
        Ahlena.API.Options.StorageRootPolicy.Validate(
            app.Environment.IsDevelopment(),
            root,
            AppContext.BaseDirectory);

        Directory.CreateDirectory(root);

        app.UseMiddleware<PrivateStorageRequestGuard>();
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider =
                    new PhysicalFileProvider(root),
                RequestPath = "/files",
                OnPrepareResponse =
                    context => ApplyStaticResponseHardening(
                        context.File.Name,
                        context.Context.Response.Headers)
            });
    }

    /// <summary>
    /// Policy served with every SVG from <c>/files</c>. SVG is active content,
    /// so it is never sniffed into another type and it runs with scripts,
    /// external loads and navigation disabled. Landing SVG bytes are sanitized
    /// before they are stored; this is defence in depth for the serving side.
    /// </summary>
    public const string SvgContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    /// <summary>
    /// Applies the SVG response hardening. Non-SVG files are left untouched so
    /// raster media keeps its current behaviour.
    /// </summary>
    public static void ApplyStaticResponseHardening(
        string? fileName,
        IHeaderDictionary headers)
    {
        if (fileName is null ||
            !fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        headers["X-Content-Type-Options"] = "nosniff";
        headers["Content-Security-Policy"] = SvgContentSecurityPolicy;
    }
}

public sealed class PrivateStorageRequestGuard : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/files/private", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
