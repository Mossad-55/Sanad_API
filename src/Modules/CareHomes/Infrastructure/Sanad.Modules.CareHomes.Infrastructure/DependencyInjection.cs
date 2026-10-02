using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.Modules.CareHomes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCareHomesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("CareHomesDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:CareHomesDatabase or ConnectionStrings:IdentityDatabase is required.");

        services.AddDbContext<CareHomesDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CareHomesDbContext.Schema)));
        services.AddScoped<ICareHomesDbContext>(provider => provider.GetRequiredService<CareHomesDbContext>());
        return services;
    }
}
