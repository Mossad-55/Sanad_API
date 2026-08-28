using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.Modules.Caregivers.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCaregiversInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString =
            configuration.GetConnectionString(
                "CaregiversDatabase")
            ?? configuration.GetConnectionString(
                "IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CaregiversDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        services.AddDbContext<CaregiversDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            CaregiversDbContext.Schema)));

        services.AddScoped<ICaregiversDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    CaregiversDbContext>());

        return services;
    }
}