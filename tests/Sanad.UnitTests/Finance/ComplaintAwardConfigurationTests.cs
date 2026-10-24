using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W4 complaint-award cap through the real Finance registration
/// (<c>Finance:ComplaintReviews:MaxAwardMinorUnits</c>).
/// <para>
/// Pinned semantics: an absent key selects the provisional default EGP 500.00
/// (50,000 piastres), an explicit positive value replaces it, an explicit
/// <c>0</c> disables the cap (<c>null</c>), and a negative or malformed raw
/// value fails startup instead of silently restoring the default or disabling
/// the guard. The cap is only ever an application-seam guard; this test drives
/// the registration, not the handler (the handler is pinned by
/// <see cref="ComplaintAwardCapTests"/>).
/// </para>
/// <para>NOT RUN: authored for the W4 gate; the Mastermind runs it.</para>
/// </summary>
public sealed class ComplaintAwardConfigurationTests
{
    private const string ConnectionStringKey = "ConnectionStrings:FinanceDatabase";
    private const string CapKey = "Finance:ComplaintReviews:MaxAwardMinorUnits";
    private const string ConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    [Theory]
    [InlineData(null, 50_000L)]     // absent -> provisional default (EGP 500.00)
    [InlineData("50000", 50_000L)]  // explicit default
    [InlineData("80000", 80_000L)]  // owner-raised ceiling
    [InlineData("0", null)]         // explicit disable
    public void Cap_resolves_from_configuration_as_documented(string? configuredValue, long? expectedCap)
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = CreateConfiguration(configuredValue);

        services.AddFinanceInfrastructure(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        ComplaintAwardOptions options = provider
            .GetRequiredService<IOptions<ComplaintAwardOptions>>()
            .Value;

        Assert.Equal(expectedCap, options.MaxAwardMinorUnits);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    [InlineData("-1")]
    [InlineData("-50000")]
    [InlineData("")]
    [InlineData("   ")]
    public void Malformed_or_negative_cap_fails_startup(string configuredValue)
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = CreateConfiguration(configuredValue);

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddFinanceInfrastructure(configuration));

        Assert.Contains("MaxAwardMinorUnits", error.Message, StringComparison.Ordinal);
        // The raw configured value is never echoed back by the parser.
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            Assert.DoesNotContain(configuredValue, error.Message, StringComparison.Ordinal);
        }
    }

    private static IConfiguration CreateConfiguration(string? configuredValue) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(ConnectionStringKey, ConnectionString),
                new KeyValuePair<string, string?>(CapKey, configuredValue)
            ])
            .Build();
}
