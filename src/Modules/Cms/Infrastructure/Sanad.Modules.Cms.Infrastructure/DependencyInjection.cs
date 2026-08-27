using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.Modules.Cms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCmsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString =
            configuration.GetConnectionString(
                "CmsDatabase")
            ?? configuration.GetConnectionString(
                "IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CmsDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        services.AddDbContext<CmsDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            CmsDbContext.Schema)));

        services.AddScoped<ICmsDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    CmsDbContext>());

        return services;
    }
}