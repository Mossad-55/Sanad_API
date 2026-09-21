using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionCouponConfiguration : IEntityTypeConfiguration<SubscriptionCoupon>
{
    public void Configure(EntityTypeBuilder<SubscriptionCoupon> builder)
    {
        builder.ToTable("subscription_coupons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PlanVersionId).HasColumnName("plan_version_id").IsRequired();
        builder.Property(x => x.DiscountPercentage).HasColumnName("discount_percentage").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.ExpiresOnUtc).HasColumnName("expires_on_utc").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_subscription_coupons_code");
        builder.HasIndex(x => x.PlanVersionId).HasDatabaseName("ix_subscription_coupons_plan_version_id");
        builder.HasOne<SubscriptionPlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
