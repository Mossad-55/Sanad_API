using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Rentals.Application;

namespace Sanad.Modules.Rentals.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRentalsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("RentalsDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:RentalsDatabase or ConnectionStrings:IdentityDatabase is required.");

        services.AddDbContext<RentalsDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", RentalsDbContext.Schema)));
        services.AddScoped<IRentalsDbContext>(provider => provider.GetRequiredService<RentalsDbContext>());
        return services;
    }
}
