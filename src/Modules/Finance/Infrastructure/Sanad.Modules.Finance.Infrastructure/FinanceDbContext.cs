using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Infrastructure.Persistence.Configurations;

namespace Sanad.Modules.Finance.Infrastructure;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options), IFinanceDbContext
{
    public const string Schema = "finance";
    public DbSet<PlatformChargeRule> PlatformChargeRules => Set<PlatformChargeRule>();
    public DbSet<CaregiverPayoutPolicy> CaregiverPayoutPolicies => Set<CaregiverPayoutPolicy>();
    public DbSet<PlatformControlState> PlatformControlStates => Set<PlatformControlState>();
    public DbSet<PlatformControlAuditEntry> PlatformControlAuditEntries => Set<PlatformControlAuditEntry>();
    public DbSet<RefundLedger> RefundLedgers => Set<RefundLedger>();
    public DbSet<RefundLedgerEntry> RefundLedgerEntries => Set<RefundLedgerEntry>();
    public DbSet<CreditBalance> CreditBalances => Set<CreditBalance>();
    public DbSet<ComplaintReview> ComplaintReviews => Set<ComplaintReview>();
    public DbSet<SubscriptionRefundRequest> SubscriptionRefundRequests => Set<SubscriptionRefundRequest>();
    public DbSet<SubscriptionRefundBudget> SubscriptionRefundBudgets => Set<SubscriptionRefundBudget>();

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
            builder.Property(x => x.DescriptionArabic).HasColumnName("description_arabic");
            builder.Property(x => x.DescriptionEnglish).HasColumnName("description_english");
            builder.HasIndex(x => x.Version).IsUnique().HasDatabaseName("ux_caregiver_payout_policy_version");
        });
        modelBuilder.Entity<PlatformControlState>(builder =>
        {
            builder.ToTable("platform_control_states", table =>
            {
                table.HasCheckConstraint("ck_platform_control_states_version", "version > 0");
                table.HasCheckConstraint("ck_platform_control_states_state", "state IN (1, 2)");
            });
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.State).HasColumnName("state").IsRequired();
            builder.Property(x => x.MaintenanceMessageArabic).HasColumnName("maintenance_message_arabic");
            builder.Property(x => x.MaintenanceMessageEnglish).HasColumnName("maintenance_message_english");
            builder.Property(x => x.CardEnabled).HasColumnName("card_enabled").IsRequired();
            builder.Property(x => x.WalletEnabled).HasColumnName("wallet_enabled").IsRequired();
            // Version is the optimistic concurrency token: a stale admin update
            // cannot silently overwrite newer state.
            builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
            builder.HasIndex(x => x.Version).IsUnique().HasDatabaseName("ux_platform_control_states_version");
        });
        modelBuilder.Entity<PlatformControlAuditEntry>(builder =>
        {
            builder.ToTable("platform_control_audit_entries", table =>
            {
                table.HasCheckConstraint("ck_platform_control_audit_entries_versions", "previous_version > 0 AND new_version = previous_version + 1");
                table.HasCheckConstraint("ck_platform_control_audit_entries_states", "before_state IN (1, 2) AND after_state IN (1, 2)");
            });
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
            builder.Property(x => x.PreviousVersion).HasColumnName("previous_version").IsRequired();
            builder.Property(x => x.NewVersion).HasColumnName("new_version").IsRequired();
            builder.Property(x => x.BeforeState).HasColumnName("before_state").IsRequired();
            builder.Property(x => x.AfterState).HasColumnName("after_state").IsRequired();
            builder.Property(x => x.BeforeMaintenanceMessageArabic).HasColumnName("before_maintenance_message_arabic");
            builder.Property(x => x.AfterMaintenanceMessageArabic).HasColumnName("after_maintenance_message_arabic");
            builder.Property(x => x.BeforeMaintenanceMessageEnglish).HasColumnName("before_maintenance_message_english");
            builder.Property(x => x.AfterMaintenanceMessageEnglish).HasColumnName("after_maintenance_message_english");
            builder.Property(x => x.BeforeCardEnabled).HasColumnName("before_card_enabled").IsRequired();
            builder.Property(x => x.AfterCardEnabled).HasColumnName("after_card_enabled").IsRequired();
            builder.Property(x => x.BeforeWalletEnabled).HasColumnName("before_wallet_enabled").IsRequired();
            builder.Property(x => x.AfterWalletEnabled).HasColumnName("after_wallet_enabled").IsRequired();
            builder.Property(x => x.InternalReason).HasColumnName("internal_reason");
            builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        });
        modelBuilder.ApplyConfiguration(new RefundLedgerConfiguration());
        modelBuilder.ApplyConfiguration(new RefundLedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CreditBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new ComplaintReviewConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionRefundRequestConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionRefundBudgetConfiguration());
    }
}
