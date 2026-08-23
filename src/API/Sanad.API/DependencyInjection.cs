using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Behaviors;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.API;

public static class DependencyInjection
{
    public static IServiceCollection AddSanadApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();

        services.AddOpenApi();

        services.AddIdentityInfrastructure(
            configuration);

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
                                    jwtOptions.SigningKey))
                    };
            });

        services.AddAuthorization();

        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(
                typeof(RegisterUserCommand).Assembly));

        services.AddValidatorsFromAssembly(
            typeof(RegisterUserCommand).Assembly);

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddExceptionHandler<
            ValidationExceptionHandler>();

        return services;
    }
}