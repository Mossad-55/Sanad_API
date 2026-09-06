using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Infrastructure;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.UnitTests.Families;

public sealed class FamiliesInfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddFamiliesInfrastructure_ShouldRejectMissingConnectionString()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = CreateConfiguration([]);

        Assert.Throws<InvalidOperationException>(
            () => services.AddFamiliesInfrastructure(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFamiliesInfrastructure_ShouldRegisterDevelopmentPaymobClient_WhenSecretKeyIsEmpty(
        string? secretKey)
    {
        IServiceCollection services =
            new ServiceCollection();

        IConfiguration configuration =
            CreateConfiguration(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:FamiliesDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>(
                    "Paymob:SecretKey",
                    secretKey)
            ]);

        services.AddFamiliesInfrastructure(
            configuration);

        using ServiceProvider serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true
                });

        Assert.IsType<DevelopmentPaymobClient>(
            serviceProvider.GetRequiredService<
                IPaymobClient>());
    }

    [Fact]
    public void AddFamiliesInfrastructure_ShouldRegisterPaymobClient_WhenSecretKeyIsSet()
    {
        IServiceCollection services =
            new ServiceCollection();

        IConfiguration configuration =
            CreateConfiguration(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:FamiliesDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>(
                    "Paymob:SecretKey",
                    "sk_test_1234567890abcdef")
            ]);

        services.AddFamiliesInfrastructure(
            configuration);

        using ServiceProvider serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true
                });

        Assert.IsType<PaymobClient>(
            serviceProvider.GetRequiredService<
                IPaymobClient>());
    }

    private static IConfiguration CreateConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
