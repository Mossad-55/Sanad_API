using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sanad.API;
using Sanad.Modules.CareHomes.Application.Bookings;

namespace Sanad.UnitTests.API;

public sealed class CareHomeBookingTimingConfigurationTests
{
    [Fact]
    public void Missing_test_clock_uses_production_booking_defaults()
    {
        CareHomeBookingTiming timing = Resolve([], Environments.Production);

        Assert.Equal(TimeSpan.FromMinutes(15), timing.CheckoutHoldDuration);
        Assert.Equal(TimeSpan.FromHours(24), timing.DecisionHoldDuration);
        Assert.Equal(TimeSpan.FromHours(24), timing.ArrivalLeadDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), timing.ExpirySweepInterval);
    }

    [Fact]
    public void Development_test_clock_accepts_positive_expiry_overrides()
    {
        CareHomeBookingTiming timing = Resolve(
            [
                Pair("CareHomes:TestClock:Enabled", "true"),
                Pair("CareHomes:TestClock:CheckoutHoldDuration", "00:00:30"),
                Pair("CareHomes:TestClock:DecisionHoldDuration", "00:01:00"),
                Pair("CareHomes:TestClock:ExpirySweepInterval", "00:00:05")
            ],
            Environments.Development);

        Assert.Equal(TimeSpan.FromSeconds(30), timing.CheckoutHoldDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), timing.DecisionHoldDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), timing.ExpirySweepInterval);
        Assert.Equal(TimeSpan.FromHours(24), timing.ArrivalLeadDuration);
    }

    [Fact]
    public void Expiry_overrides_without_explicit_test_clock_are_rejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(
                [Pair("CareHomes:TestClock:CheckoutHoldDuration", "00:00:30")],
                Environments.Development));

        Assert.Contains("Enabled=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_test_clock_outside_development_is_rejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(
                [Pair("CareHomes:TestClock:Enabled", "true")],
                Environments.Production));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CheckoutHoldDuration", "00:00:00")]
    [InlineData("CheckoutHoldDuration", "-00:00:01")]
    [InlineData("DecisionHoldDuration", "00:00:00")]
    [InlineData("DecisionHoldDuration", "-00:00:01")]
    [InlineData("ExpirySweepInterval", "00:00:00")]
    [InlineData("ExpirySweepInterval", "-00:00:01")]
    public void Enabled_test_clock_rejects_non_positive_expiry_durations(string key, string duration)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(
                [
                    Pair("CareHomes:TestClock:Enabled", "true"),
                    Pair($"CareHomes:TestClock:{key}", duration)
                ],
                Environments.Development));

        Assert.Contains("must be positive", exception.Message, StringComparison.Ordinal);
    }

    private static CareHomeBookingTiming Resolve(
        IEnumerable<KeyValuePair<string, string?>> values,
        string environmentName)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        MethodInfo method = typeof(DependencyInjection).GetMethod(
            "ResolveCareHomeBookingTiming",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Timing resolver was not found.");

        try
        {
            return Assert.IsType<CareHomeBookingTiming>(
                method.Invoke(null, [configuration, new TestHostEnvironment(environmentName)]));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static KeyValuePair<string, string?> Pair(string key, string value) => new(key, value);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sanad.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
