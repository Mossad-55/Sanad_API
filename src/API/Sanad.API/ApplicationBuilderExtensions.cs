namespace Sanad.API;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Seeding;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseSanadApi(
        this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseStatusCodePages();

        UseLocalFiles(app);

        app.UseHttpsRedirection();

        app.UseAuthentication();

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

        ApplyIdentityMigrations(app);
        ApplyCmsMigrations(app);

        SeedSuperAdmin(app);

        return app;
    }

    private static void ApplyIdentityMigrations(
        WebApplication app)
    {
        using IServiceScope scope =
            app.Services.CreateScope();

        IdentityDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<
                IdentityDbContext>();

        dbContext.Database.Migrate();
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

    private static void ApplyCmsMigrations(
        WebApplication app)
    {
        using IServiceScope scope =
            app.Services.CreateScope();

        CmsDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<
                CmsDbContext>();

        dbContext.Database.Migrate();
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

        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider =
                    new PhysicalFileProvider(root),
                RequestPath = "/files"
            });
    }
}