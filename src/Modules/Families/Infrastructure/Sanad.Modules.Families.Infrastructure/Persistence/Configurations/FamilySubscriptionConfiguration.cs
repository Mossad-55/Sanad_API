using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class FamilySubscriptionConfiguration : IEntityTypeConfiguration<FamilySubscription>
{
    public void Configure(EntityTypeBuilder<FamilySubscription> builder)
    {
        builder.ToTable("family_subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.PlanKey).HasColumnName("plan_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlanVersion).HasColumnName("plan_version").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Cycle).HasColumnName("cycle").HasConversion<int>().IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.MemberLimitKind).HasColumnName("member_limit_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.MemberLimitValue).HasColumnName("member_limit_value");
        builder.Property(x => x.MonthlyBookingLimitKind).HasColumnName("monthly_booking_limit_kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.MonthlyBookingLimitValue).HasColumnName("monthly_booking_limit_value");
        builder.Property(x => x.Rollover).HasColumnName("rollover").HasConversion<int>().IsRequired();
        builder.Property(x => x.IsCurrent).HasColumnName("is_current").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.HasIndex(x => x.FamilyId).HasFilter("is_current = true").IsUnique().HasDatabaseName("ux_family_subscriptions_current");
        builder.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(x => x.Benefits, benefit =>
        {
            benefit.ToTable("family_subscription_benefits");
            benefit.WithOwner().HasForeignKey("FamilySubscriptionId");
            benefit.Property<Guid>("FamilySubscriptionId").HasColumnName("family_subscription_id");
            benefit.Property(x => x.Key).HasColumnName("benefit_key").HasConversion<int>().IsRequired();
            benefit.Property(x => x.IsIncluded).HasColumnName("is_included").IsRequired();
            benefit.HasKey("FamilySubscriptionId", nameof(SubscriptionBenefit.Key));
        });
    }
}
