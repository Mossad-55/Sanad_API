using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanVersionConfiguration : IEntityTypeConfiguration<SubscriptionPlanVersion>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanVersion> builder)
    {
        builder.ToTable("subscription_plan_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Key).HasColumnName("plan_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Cycle).HasColumnName("cycle").HasConversion<int>().IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.MemberLimitKind).HasColumnName("member_limit_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.MemberLimitValue).HasColumnName("member_limit_value");
        builder.Property(x => x.MonthlyBookingLimitKind).HasColumnName("monthly_booking_limit_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.MonthlyBookingLimitValue).HasColumnName("monthly_booking_limit_value");
        builder.Property(x => x.Rollover).HasColumnName("rollover").HasConversion<int>().IsRequired();
        builder.Property(x => x.IsPublished).HasColumnName("is_published").IsRequired();
        builder.Property(x => x.IsAvailableForNewSales).HasColumnName("is_available_for_new_sales").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.PublishedOnUtc).HasColumnName("published_on_utc");
        builder.HasIndex(x => new { x.Key, x.Version }).IsUnique().HasDatabaseName("ux_subscription_plan_versions_key_version");

        builder.OwnsMany(x => x.Benefits, benefit =>
        {
            benefit.ToTable("subscription_plan_version_benefits");
            benefit.WithOwner().HasForeignKey("SubscriptionPlanVersionId");
            benefit.Property<Guid>("SubscriptionPlanVersionId").HasColumnName("subscription_plan_version_id");
            benefit.Property(x => x.Key).HasColumnName("benefit_key").HasConversion<int>().IsRequired();
            benefit.Property(x => x.IsIncluded).HasColumnName("is_included").IsRequired();
            benefit.HasKey("SubscriptionPlanVersionId", nameof(SubscriptionBenefit.Key));
        });
    }
}
