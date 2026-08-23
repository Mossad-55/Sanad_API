using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Identity.Application.Abstractions.Security;

namespace Sanad.UnitTests.API;

public sealed class SanadApiDependencyInjectionTests
{
    [Fact]
    public void AddSanadApi_ShouldRejectMissingJwtConfiguration()
    {
        IServiceCollection services =
            new ServiceCollection();

        IConfiguration configuration =
            CreateConfiguration(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test")
            ]);

        Assert.Throws<InvalidOperationException>(
            () => services.AddSanadApi(
                configuration));
    }

    [Fact]
    public void AddSanadApi_ShouldRegisterInfrastructureAndAuthentication()
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
                    "Sanad.Api"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Audience",
                    "Sanad.Clients"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ]);

        services.AddSanadApi(
            configuration);

        using ServiceProvider provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true
                });

        using IServiceScope scope =
            provider.CreateScope();

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                IAuthTokenService>());

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                IAuthTokenService>());

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                IDateTimeProvider>());
    }

    private static IConfiguration CreateConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}