namespace Sanad.API;

using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Infrastructure.Persistence;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseSanadApi(
        this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseStatusCodePages();

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
}