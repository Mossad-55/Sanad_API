using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Messaging;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

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
    public void AddIdentityInfrastructure_ShouldResolveAllProviders()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = CreateValidConfiguration();

        services.AddIdentityInfrastructure(configuration);

        using ServiceProvider serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true
            });

        using IServiceScope scope = serviceProvider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IIdentityDbContext>());
        Assert.IsType<AspNetPasswordHasher>(scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        Assert.IsType<Pbkdf2OtpService>(scope.ServiceProvider.GetRequiredService<IOtpService>());
        Assert.IsType<JwtAuthTokenService>(scope.ServiceProvider.GetRequiredService<IAuthTokenService>());
        Assert.IsType<DevelopmentEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<DevelopmentSmsSender>(scope.ServiceProvider.GetRequiredService<ISmsSender>());
        Assert.IsType<DevelopmentExternalIdentityVerifier>(scope.ServiceProvider.GetRequiredService<IExternalIdentityVerifier>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISocialAuthenticationChallengeStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISocialRegistrationChallengeStore>());
    }

    [Theory]
    [InlineData("", "sanad-clients", "12345678901234567890123456789012")]
    [InlineData("sanad-api", "", "12345678901234567890123456789012")]
    [InlineData("sanad-api", "sanad-clients", "short")]
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
                "sanad-api"),
            new KeyValuePair<string, string?>(
                "Identity:Jwt:Audience",
                "sanad-clients"),
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
