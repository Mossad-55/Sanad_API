using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sanad.API;

namespace Sanad.UnitTests.API;

public sealed class StartupMigrationPolicyTests
{
    [Fact]
    public void Module_migrations_default_to_enabled()
    {
        Assert.True(StartupMigrationPolicy.ShouldApplyModuleMigrations(
            Configuration([]),
            new TestHostEnvironment(Environments.Production)));
    }

    [Fact]
    public void Production_cannot_disable_module_migrations()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => StartupMigrationPolicy.ShouldApplyModuleMigrations(
                Configuration([Pair("Database:ApplyMigrationsOnStartup", "false")]),
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("cannot be disabled outside Development", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_can_disable_module_migrations_for_host_tests()
    {
        Assert.False(StartupMigrationPolicy.ShouldApplyModuleMigrations(
            Configuration([Pair("Database:ApplyMigrationsOnStartup", "false")]),
            new TestHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void Finance_migrations_are_automatic_outside_development()
    {
        Assert.True(StartupMigrationPolicy.ShouldApplyFinanceMigrations(
            Configuration([Pair("FinanceMigrations:ApplyOnStartup", "false")]),
            new TestHostEnvironment(Environments.Production)));
    }

    [Fact]
    public void Development_finance_migrations_remain_opt_in()
    {
        Assert.False(StartupMigrationPolicy.ShouldApplyFinanceMigrations(
            Configuration([]),
            new TestHostEnvironment(Environments.Development)));

        Assert.True(StartupMigrationPolicy.ShouldApplyFinanceMigrations(
            Configuration([Pair("FinanceMigrations:ApplyOnStartup", "true")]),
            new TestHostEnvironment(Environments.Development)));
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

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
