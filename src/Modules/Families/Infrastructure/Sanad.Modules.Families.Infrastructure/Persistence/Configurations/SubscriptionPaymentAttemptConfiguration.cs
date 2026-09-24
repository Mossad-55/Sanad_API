using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPaymentAttemptConfiguration : IEntityTypeConfiguration<SubscriptionPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentAttempt> builder)
    {
        builder.ToTable("subscription_payment_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.PlanVersionId).HasColumnName("plan_version_id").IsRequired();
        builder.Property(x => x.PlanKey).HasColumnName("plan_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlanVersion).HasColumnName("plan_version").IsRequired();
        builder.Property(x => x.BasePrice).HasColumnName("base_price").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DiscountPercentage).HasColumnName("discount_percentage").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxRatePercentage).HasColumnName("tax_rate_percentage").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalPayable).HasColumnName("total_payable").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<int>().IsRequired();
        builder.Property(x => x.CouponCode).HasColumnName("coupon_code").HasMaxLength(50);
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(x => x.IsRenewal).HasColumnName("is_renewal").IsRequired();
        builder.Property(x => x.IsPlanChange).HasColumnName("is_plan_change").IsRequired();
        builder.Property(x => x.SourcePeriodGross).HasColumnName("source_period_gross").HasPrecision(18, 2);
        builder.Property(x => x.SourcePeriodTaxRatePercentage).HasColumnName("source_period_tax_rate_percentage").HasPrecision(5, 2);
        builder.Property(x => x.ProratedCredit).HasColumnName("prorated_credit").HasPrecision(18, 2);
        builder.Property(x => x.SourcePeriodEndsOnUtc).HasColumnName("source_period_ends_on_utc");
        builder.Ignore(x => x.MerchantReference);
        builder.Property(x => x.PaymobOrderId).HasColumnName("paymob_order_id").HasMaxLength(100);
        builder.Property(x => x.PaymobTransactionId).HasColumnName("paymob_transaction_id").HasMaxLength(100);
        builder.Property(x => x.PaymobInitialTransactionId).HasColumnName("paymob_initial_transaction_id").HasMaxLength(100);
        builder.Property(x => x.PaymobSubscriptionId).HasColumnName("paymob_subscription_id").HasMaxLength(100);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.SettledOnUtc).HasColumnName("settled_on_utc");
        builder.Property(x => x.FailedOnUtc).HasColumnName("failed_on_utc");
        builder.HasIndex(x => x.PaymobOrderId).IsUnique();
        builder.HasIndex(x => x.PaymobSubscriptionId)
            .HasFilter("paymob_subscription_id IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("ux_subscription_payment_attempts_paymob_subscription_id");
        builder.HasIndex(x => new { x.FamilyId, x.Status });
        builder.HasIndex(x => new { x.SubscriptionId, x.IsRenewal, x.Status });
        builder.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SubscriptionPlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilySubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
