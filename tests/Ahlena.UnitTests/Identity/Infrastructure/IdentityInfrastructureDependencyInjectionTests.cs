using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ahlena.Modules.Identity.Infrastructure;
using Ahlena.Modules.Identity.Infrastructure.Security;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Infrastructure.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;

namespace Ahlena.UnitTests.Identity.Infrastructure;

public sealed class IdentityInfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddIdentityInfrastructure_ShouldRejectMissingConnectionString()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = CreateConfiguration([]);

        Assert.Throws<InvalidOperationException>(
            () => services.AddIdentityInfrastructure(configuration));
    }

    [Fact]
    public void AddIdentityInfrastructure_ShouldResolveRemainingProviders()
    {
        IServiceCollection services =
            new ServiceCollection();

        IConfiguration configuration =
            CreateValidConfiguration();

        services.AddIdentityInfrastructure(
            configuration);

        using ServiceProvider serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes =
                        true
                });

        using IServiceScope scope =
            serviceProvider.CreateScope();

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                IIdentityDbContext>());

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                IDateTimeProvider>());

        Assert.IsType<AspNetPasswordHasher>(
            scope.ServiceProvider.GetRequiredService<
                IPasswordHasher>());

        Assert.IsType<Pbkdf2OtpService>(
            scope.ServiceProvider.GetRequiredService<
                IOtpService>());

        Assert.IsType<JwtAuthTokenService>(
            scope.ServiceProvider.GetRequiredService<
                IAuthTokenService>());

        Assert.IsType<DevelopmentEmailSender>(
            scope.ServiceProvider.GetRequiredService<
                IEmailSender>());

        Assert.IsType<DevelopmentSmsSender>(
            scope.ServiceProvider.GetRequiredService<
                ISmsSender>());
    }

    [Fact]
    public void AddIdentityInfrastructure_ShouldResolveConfiguredMessagingProviders()
    {
        IServiceCollection services =
            new ServiceCollection();

        IConfiguration configuration =
            CreateConfiguration(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Issuer",
                    "ahlena-api"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Audience",
                    "ahlena-clients"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012"),
                new KeyValuePair<string, string?>(
                    "Identity:Email:Smtp:Host",
                    "smtp.example.com"),
                new KeyValuePair<string, string?>(
                    "Identity:Email:Smtp:FromAddress",
                    "noreply@example.com"),
                new KeyValuePair<string, string?>(
                    "Identity:Sms:SmsMisr:Username",
                    "test-user"),
                new KeyValuePair<string, string?>(
                    "Identity:Sms:SmsMisr:Password",
                    "test-password"),
                new KeyValuePair<string, string?>(
                    "Identity:Sms:SmsMisr:Sender",
                    "test-sender"),
            ]);

        services.AddIdentityInfrastructure(
            configuration);

        using ServiceProvider serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes =
                        true
                });

        using IServiceScope scope =
            serviceProvider.CreateScope();

        Assert.IsType<SmtpEmailSender>(
            scope.ServiceProvider.GetRequiredService<
                IEmailSender>());

        Assert.IsType<SmsMisrSmsSender>(
            scope.ServiceProvider.GetRequiredService<
                ISmsSender>());
    }

    [Theory]
    [InlineData("", "ahlena-clients", "12345678901234567890123456789012")]
    [InlineData("ahlena-api", "", "12345678901234567890123456789012")]
    [InlineData("ahlena-api", "ahlena-clients", "short")]
    public void JwtOptions_ShouldRejectInvalidConfiguration(
        string issuer,
        string audience,
        string signingKey)
    {
        IServiceCollection services = new ServiceCollection();

        services.AddIdentityInfrastructure(
            CreateConfiguration(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Issuer",
                    issuer),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Audience",
                    audience),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    signingKey)
            ]));

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<JwtOptions>>().Value);
    }

    private static IConfiguration CreateValidConfiguration()
    {
        return CreateConfiguration(
        [
            new KeyValuePair<string, string?>(
                "ConnectionStrings:IdentityDatabase",
                "Host=localhost;Database=test;Username=test;Password=test"),
            new KeyValuePair<string, string?>(
                "Identity:Jwt:Issuer",
                "ahlena-api"),
            new KeyValuePair<string, string?>(
                "Identity:Jwt:Audience",
                "ahlena-clients"),
            new KeyValuePair<string, string?>(
                "Identity:Jwt:SigningKey",
                "12345678901234567890123456789012")
        ]);
    }

    private static IConfiguration CreateConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
