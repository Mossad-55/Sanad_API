using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Infrastructure.Challenges;
using Sanad.Modules.Identity.Infrastructure.Messaging;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Security;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Infrastructure.Time;
using Sanad.Modules.Identity.Infrastructure.Nonces;

namespace Sanad.Modules.Identity.Infrastructure;

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

        services.AddSingleton<
            IValidateOptions<JwtOptions>,
            JwtOptionsValidator>();

        services.AddSingleton<IPasswordHasher,
            AspNetPasswordHasher>();

        services.AddSingleton<IOtpService,
            Pbkdf2OtpService>();

        services.AddSingleton<IAuthTokenService,
            JwtAuthTokenService>();

        services.AddSingleton<IEmailSender,
            DevelopmentEmailSender>();

        services.AddSingleton<ISmsSender,
            DevelopmentSmsSender>();

        services.AddSingleton<IExternalIdentityVerifier,
            DevelopmentExternalIdentityVerifier>();

        services.AddScoped<
            IExternalAuthenticationNonceStore,
            PostgresExternalAuthenticationNonceStore>();

        services.AddScoped<
            ISocialAuthenticationChallengeStore,
            PostgresSocialAuthenticationChallengeStore>();

        services.AddScoped<
            ISocialRegistrationChallengeStore,
            PostgresSocialRegistrationChallengeStore>();

        return services;
    }
}