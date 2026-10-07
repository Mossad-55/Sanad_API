namespace Sanad.API;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sanad.Modules.Finance.Infrastructure;

public static class StartupMigrationPolicy
{
    public static bool ShouldApplyModuleMigrations(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        bool applyMigrations = configuration.GetValue(
            "Database:ApplyMigrationsOnStartup",
            defaultValue: true);

        if (!applyMigrations && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Database:ApplyMigrationsOnStartup cannot be disabled outside Development.");
        }

        return applyMigrations;
    }

    public static bool ShouldApplyFinanceMigrations(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsDevelopment() ||
        configuration.GetValue<bool>(
            $"{FinanceMigrationOptions.SectionName}:ApplyOnStartup");
}
