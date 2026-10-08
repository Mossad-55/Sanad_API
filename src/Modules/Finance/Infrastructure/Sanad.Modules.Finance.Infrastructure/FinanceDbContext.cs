using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Finance.Infrastructure;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options), IFinanceDbContext
{
    public const string Schema = "finance";
    public DbSet<PlatformChargeRule> PlatformChargeRules => Set<PlatformChargeRule>();
    public DbSet<CaregiverPayoutPolicy> CaregiverPayoutPolicies => Set<CaregiverPayoutPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<PlatformChargeRule>(builder =>
        {
            builder.ToTable("platform_charge_rules", table =>
            {
                table.HasCheckConstraint("ck_platform_charge_rules_fee_rate", "platform_fee_rate_percentage BETWEEN 0 AND 100");
                table.HasCheckConstraint("ck_platform_charge_rules_tax_rate", "tax_rate_percentage BETWEEN 0 AND 100");
                table.HasCheckConstraint("ck_platform_charge_rules_version", "version > 0");
            });
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.PlatformFeeRatePercentage).HasColumnName("platform_fee_rate_percentage").HasPrecision(5, 2).IsRequired();
            builder.Property(x => x.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasPrecision(5, 2).IsRequired();
            builder.Property(x => x.Version).HasColumnName("version").IsRequired();
            builder.Property(x => x.EffectiveOnUtc).HasColumnName("effective_on_utc").IsRequired();
            builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
            builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            builder.HasIndex(x => x.Version).IsUnique().HasDatabaseName("ux_platform_charge_rules_version");
            builder.HasIndex(x => x.IsActive).HasFilter("\"is_active\" = TRUE").IsUnique().HasDatabaseName("ux_platform_charge_rules_active");
        });
        modelBuilder.Entity<CaregiverPayoutPolicy>(builder =>
        {
            builder.ToTable("caregiver_payout_policies", table =>
            {
                table.HasCheckConstraint("ck_caregiver_payout_policy_delay", "payout_delay_hours >= 0");
                table.HasCheckConstraint("ck_caregiver_payout_policy_version", "version > 0");
            });
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.PayoutDelayHours).HasColumnName("payout_delay_hours").IsRequired();
            builder.Property(x => x.Version).HasColumnName("version").IsRequired();
            builder.Property(x => x.EffectiveOnUtc).HasColumnName("effective_on_utc").IsRequired();
            builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
            builder.HasIndex(x => x.Version).IsUnique().HasDatabaseName("ux_caregiver_payout_policy_version");
        });
    }
}
