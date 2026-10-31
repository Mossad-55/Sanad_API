using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Infrastructure.Messaging;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Security;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Infrastructure.Time;
using Ahlena.Modules.Identity.Infrastructure.Persistence.Seeding;

namespace Ahlena.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString =
            configuration.GetConnectionString(
                "IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:IdentityDatabase " +
                "is required.");
        }

        services.AddDbContext<IdentityDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            IdentityDbContext.Schema)));

        services.AddScoped<IIdentityDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    IdentityDbContext>());

        services.AddSingleton<
            IDateTimeProvider,
            SystemDateTimeProvider>();

        services.AddOptions<JwtOptions>()
            .Bind(
                configuration.GetSection(
                    JwtOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AdminSeedOptions>()
            .Bind(
                configuration.GetSection(
                    AdminSeedOptions.SectionName));

        services.AddScoped<SuperAdminSeeder>();

        services.AddOptions<FinanceAdminSeedOptions>()
            .Bind(
                configuration.GetSection(
                    FinanceAdminSeedOptions.SectionName));

        services.AddScoped<FinanceAdminSeeder>();

        services.AddSingleton<
            IValidateOptions<JwtOptions>,
            JwtOptionsValidator>();

        services.AddSingleton<IPasswordHasher,
            AspNetPasswordHasher>();

        services.AddSingleton<IOtpService,
            Pbkdf2OtpService>();

        services.AddSingleton<IAuthTokenService,
            JwtAuthTokenService>();

        services.AddOptions<EmailOptions>()
            .Bind(
                configuration.GetSection(
                    EmailOptions.SectionName));

        services.AddOptions<SmsMisrOptions>()
            .Bind(
                configuration.GetSection(
                    SmsMisrOptions.SectionName));

        if (IsEmailConfigured(configuration))
        {
            services.AddSingleton<IEmailSender,
                SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender,
                DevelopmentEmailSender>();
        }

        if (IsSmsMisrConfigured(configuration))
        {
            services.AddHttpClient<ISmsSender, SmsMisrSmsSender>(
                client =>
                {
                    client.Timeout =
                        TimeSpan.FromSeconds(30);
                });
        }
        else
        {
            services.AddSingleton<ISmsSender,
                DevelopmentSmsSender>();
        }

        return services;
    }

    private static bool IsEmailConfigured(
        IConfiguration configuration)
    {
        IConfiguration section =
            configuration.GetSection(
                EmailOptions.SectionName);

        return !string.IsNullOrWhiteSpace(
                   section["Host"]) &&
               !string.IsNullOrWhiteSpace(
                   section["FromAddress"]);
    }

    private static bool IsSmsMisrConfigured(
        IConfiguration configuration)
    {
        IConfiguration section =
            configuration.GetSection(
                SmsMisrOptions.SectionName);

        return !string.IsNullOrWhiteSpace(
                   section["Username"]) &&
               !string.IsNullOrWhiteSpace(
                   section["Password"]) &&
               !string.IsNullOrWhiteSpace(
                   section["Sender"]);
    }
}