using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanRetirementAuditConfiguration : IEntityTypeConfiguration<SubscriptionPlanRetirementAudit>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanRetirementAudit> builder)
    {
        builder.ToTable("subscription_plan_retirement_audits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PlanVersionId).HasColumnName("plan_version_id").IsRequired();
        builder.Property(x => x.PlanKey).HasColumnName("plan_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlanVersion).HasColumnName("plan_version").IsRequired();
        builder.Property(x => x.ActorUserId).HasConversion(id => id.Value, value => new UserId(value)).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(50).IsRequired();
        builder.Property(x => x.OldAvailability).HasColumnName("old_availability").IsRequired();
        builder.Property(x => x.NewAvailability).HasColumnName("new_availability").IsRequired();
        builder.Property(x => x.RetiredOnUtc).HasColumnName("retired_on_utc").IsRequired();
        builder.HasIndex(x => x.PlanVersionId).HasDatabaseName("ix_subscription_plan_retirement_audits_plan_version_id");
        builder.HasOne<SubscriptionPlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
