using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sanad.API.Authorization;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Behaviors;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Security;
using Sanad.Modules.Cms.Infrastructure;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Cms.Application.Splash;
using Sanad.Modules.Caregivers.Infrastructure;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.Modules.Caregivers.Application.Lookups;

namespace Sanad.API;

public static class DependencyInjection
{
    public static IServiceCollection AddSanadApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();

        // Temporary: allow any client during mobile development.
        // Lock down to known origins before production launch.
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        services.AddProblemDetails();

        services.AddOpenApi();

        services.AddIdentityInfrastructure(
            configuration);

        services.AddCmsInfrastructure(
            configuration);

        services.AddCaregiversInfrastructure(
            configuration);

        services.AddOptions<LocalStorageOptions>()
            .Bind(
                configuration.GetSection(
                    LocalStorageOptions.SectionName));

        services.AddSingleton<IFileStorage,
            LocalDiskFileStorage>();

        JwtOptions jwtOptions =
            configuration
                .GetSection(
                    JwtOptions.SectionName)
                .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "Identity:Jwt configuration is required.");

        if (string.IsNullOrWhiteSpace(
                jwtOptions.SigningKey) ||
            Encoding.UTF8.GetByteCount(
                jwtOptions.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Identity:Jwt:SigningKey must contain " +
                "at least 32 UTF-8 bytes.");
        }

        services.AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims =
                    false;

                options.RequireHttpsMetadata =
                    !string.Equals(
                        Environment.GetEnvironmentVariable(
                            "ASPNETCORE_ENVIRONMENT"),
                        "Development",
                        StringComparison.OrdinalIgnoreCase);

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtOptions.Issuer,

                        ValidateAudience = true,
                        ValidAudience = jwtOptions.Audience,

                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,

                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(
                                    jwtOptions.SigningKey)),

                        NameClaimType =
                            JwtRegisteredClaimNames.Sub,

                        RoleClaimType =
                            AuthClaimNames.AccountType
                    };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.NormalAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CmsContent,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.ContentAdmin.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CaregiversAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.ContentAdmin.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CaregiverAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.MedicalCaregiver.ToString(),
                        AccountType.CompanionCaregiver.ToString());
                });
        });

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(
                typeof(RegisterUserCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(CreateSplashScreenCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(CreateServiceCommand).Assembly);
        });


        services.AddValidatorsFromAssembly(
            typeof(RegisterUserCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(CreateSplashScreenCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(CreateServiceCommand).Assembly);

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddExceptionHandler<
            ValidationExceptionHandler>();

        return services;
    }
}