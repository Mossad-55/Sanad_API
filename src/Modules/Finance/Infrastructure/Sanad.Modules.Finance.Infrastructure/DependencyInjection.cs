using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FinanceDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("ConnectionStrings:FinanceDatabase or ConnectionStrings:IdentityDatabase is required.");
        services.AddDbContext<FinanceDbContext>(options => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)));
        bool applyOnStartup = bool.TryParse(
            configuration[$"{FinanceMigrationOptions.SectionName}:ApplyOnStartup"],
            out bool configuredApplyOnStartup) && configuredApplyOnStartup;
        services.AddSingleton<IOptions<FinanceMigrationOptions>>(
            Options.Create(new FinanceMigrationOptions { ApplyOnStartup = applyOnStartup }));
        services.AddScoped<IFinanceDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
        services.AddScoped<PlatformChargeRuleReader>();
        services.AddScoped<IPlatformChargeRuleReader>(sp => sp.GetRequiredService<PlatformChargeRuleReader>());
        services.AddScoped<IPlatformChargeRuleWriter>(sp => sp.GetRequiredService<PlatformChargeRuleReader>());
        return services;
    }
}
