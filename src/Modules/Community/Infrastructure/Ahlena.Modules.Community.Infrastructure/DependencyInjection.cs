using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.Modules.Community.Infrastructure;

public static class CommunityInfrastructure
{
    public static IServiceCollection AddCommunityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("CommunityDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CommunityDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        services.AddDbContext<CommunityDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            CommunityDbContext.Schema)));

        services.AddScoped<ICommunityDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    CommunityDbContext>());

        return services;
    }
}
