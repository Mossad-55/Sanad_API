using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Infrastructure.Security;

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

        // Payout IBAN protection keys come only from host configuration
        // (environment). Absent configuration is valid at startup so other
        // features keep running; payout-account writes fail closed at use.
        services.AddOptions<PayoutIbanProtectionOptions>()
            .Bind(
                configuration.GetSection(
                    PayoutIbanProtectionOptions.SectionName));

        services.AddSingleton<
            IValidateOptions<PayoutIbanProtectionOptions>,
            PayoutIbanProtectionOptionsValidator>();

        services.AddSingleton<IIbanProtector, AesGcmIbanProtector>();

        return services;
    }
}