namespace Sanad.API;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Sanad.API.Seeding;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Seeding;
using Sanad.Modules.Notifications.Infrastructure.Persistence;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Finance.Infrastructure;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseSanadApi(
        this WebApplication app)
    {
        TestUserSeedTargetGuard.EnsureSafeTarget(
            app.Configuration,
            app.Environment);

        app.UseExceptionHandler();

        app.UseStatusCodePages();

        UseLocalFiles(app);

        app.UseHttpsRedirection();

        app.UseAuthentication();

        app.UseCors();

        app.UseAuthorization();

        app.MapControllers();

        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/openapi/v1.json",
                    "Sanad Care API v1");
            });
        }

        if (StartupMigrationPolicy.ShouldApplyModuleMigrations(
                app.Configuration,
                app.Environment))
        {
            ApplyMigrations<IdentityDbContext>(app);
            ApplyMigrations<CmsDbContext>(app);
            ApplyMigrations<CaregiversDbContext>(app);
            ApplyMigrations<FamiliesDbContext>(app);
            ApplyMigrations<CareHomesDbContext>(app);
            ApplyMigrations<NotificationsDbContext>(app);
            ApplyMigrations<CommunityDbContext>(app);
            if (StartupMigrationPolicy.ShouldApplyFinanceMigrations(
                    app.Configuration,
                    app.Environment))
            {
                ApplyMigrations<FinanceDbContext>(app);
            }

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
            .CreateLogger("Sanad.DatabaseMigrations");

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

        Directory.CreateDirectory(root);

        app.UseMiddleware<PrivateStorageRequestGuard>();
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider =
                    new PhysicalFileProvider(root),
                RequestPath = "/files"
            });
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
