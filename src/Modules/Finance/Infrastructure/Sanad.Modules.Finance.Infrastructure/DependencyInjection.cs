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
        // W3-F3 complaint award cap (Owner-ordered provisional default EGP
        // 500.00, 2026-10-23). Configuration:
        // Finance:ComplaintReviews:MaxAwardMinorUnits — absent selects the
        // default, an explicit 0 disables the cap, and a negative or malformed
        // value fails startup via ParseConfiguredValue instead of silently
        // restoring the default (or disabling the cap).
        services.AddSingleton<IOptions<ComplaintAwardOptions>>(
            Options.Create(ComplaintAwardOptions.FromConfiguredValue(
                ComplaintAwardOptions.ParseConfiguredValue(
                    configuration["Finance:ComplaintReviews:MaxAwardMinorUnits"]))));
        services.AddScoped<IFinanceDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
        services.AddScoped<PlatformChargeRuleReader>();
        services.AddScoped<IPlatformChargeRuleReader>(sp => sp.GetRequiredService<PlatformChargeRuleReader>());
        services.AddScoped<IPlatformChargeRuleWriter>(sp => sp.GetRequiredService<PlatformChargeRuleReader>());
        services.AddScoped<CaregiverPayoutPolicyStore>();
        services.AddScoped<ICaregiverPayoutPolicyReader>(sp => sp.GetRequiredService<CaregiverPayoutPolicyStore>());
        services.AddScoped<ICaregiverPayoutPolicyWriter>(sp => sp.GetRequiredService<CaregiverPayoutPolicyStore>());
        services.AddScoped<PlatformControlStore>();
        services.AddScoped<IPlatformControlReader>(sp => sp.GetRequiredService<PlatformControlStore>());
        services.AddScoped<IPlatformControlWriter>(sp => sp.GetRequiredService<PlatformControlStore>());
        services.AddScoped<FamilyCreditBalanceStore>();
        services.AddScoped<IFamilyCreditBalanceReader>(sp => sp.GetRequiredService<FamilyCreditBalanceStore>());
        services.AddScoped<IFamilyCreditBalanceWriter>(sp => sp.GetRequiredService<FamilyCreditBalanceStore>());
        // W3-F3 additive seams: the reviewed-complaint workflow and the M7
        // idempotent subscription money-movement recorder. Consumers reference
        // Finance.Application only; the implementations are built here, which is
        // the single Finance module registration (W3 design pin item 2).
        services.AddScoped<ComplaintReviewStore>();
        services.AddScoped<IComplaintReviewStore>(sp => sp.GetRequiredService<ComplaintReviewStore>());
        services.AddScoped<IComplaintReviewReader>(sp => sp.GetRequiredService<ComplaintReviewStore>());
        services.AddScoped<SubscriptionRefundRecorder>();
        services.AddScoped<ISubscriptionRefundRecorder>(sp => sp.GetRequiredService<SubscriptionRefundRecorder>());
        return services;
    }
}
