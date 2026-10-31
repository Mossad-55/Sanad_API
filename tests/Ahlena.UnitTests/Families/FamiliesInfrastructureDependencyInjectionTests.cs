using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Infrastructure;
using Ahlena.Modules.Families.Infrastructure.Payments;

namespace Ahlena.UnitTests.Families;

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

        // The non-keyed client is the seam decorator (pass-through while
        // disabled); the keyed inner client is the Development client.
        Assert.IsType<FixtureRefundSeamPaymobClient>(
            serviceProvider.GetRequiredService<
                IPaymobClient>());
        Assert.IsType<DevelopmentPaymobClient>(
            serviceProvider.GetRequiredKeyedService<
                IPaymobClient>("paymob-inner"));
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

        // The non-keyed client is the seam decorator (pass-through while
        // disabled); the keyed inner client is the REAL provider client.
        Assert.IsType<FixtureRefundSeamPaymobClient>(
            serviceProvider.GetRequiredService<
                IPaymobClient>());
        Assert.IsType<PaymobClient>(
            serviceProvider.GetRequiredKeyedService<
                IPaymobClient>("paymob-inner"));
    }

    private static IConfiguration CreateConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
