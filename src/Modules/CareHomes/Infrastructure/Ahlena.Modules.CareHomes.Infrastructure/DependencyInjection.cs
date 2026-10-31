using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Inventory;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Application.Visits;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.CareHomes.Infrastructure.Bookings;
using Ahlena.Modules.CareHomes.Infrastructure.Visits;

namespace Ahlena.Modules.CareHomes.Infrastructure;

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
        services.AddScoped<ICareHomeOccupancyProvider, CareHomeBookingOccupancyProvider>();
        services.AddScoped<ICareHomeBookingReservationGuard, CareHomeBookingReservationGuard>();
        services.AddScoped<ICareHomeVisitReservationGuard, CareHomeVisitReservationGuard>();
        return services;
    }
}
