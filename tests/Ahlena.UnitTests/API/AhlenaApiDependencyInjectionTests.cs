using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.API;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.Modules.Identity.Application.Abstractions.Security;

namespace Ahlena.UnitTests.API;

public sealed class AhlenaApiDependencyInjectionTests
{
    [Fact]
    public void AddAhlenaApi_ShouldRejectMissingJwtConfiguration()
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
            () => services.AddAhlenaApi(
                configuration));
    }

    [Fact]
    public void AddAhlenaApi_ShouldRegisterInfrastructureAndAuthentication()
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
                    "Ahlena.Api"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Audience",
                    "Ahlena.Clients"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ]);

        services.AddAhlenaApi(
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