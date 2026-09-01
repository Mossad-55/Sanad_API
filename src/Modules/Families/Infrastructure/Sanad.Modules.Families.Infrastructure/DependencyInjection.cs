using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.Modules.Families.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFamiliesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString =
            configuration.GetConnectionString("FamiliesDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:FamilyDatabase or IdentityDatabase is required.");

        services.AddDbContext<FamiliesDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IFamiliesDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<FamiliesDbContext>());

        return services;
    }
}