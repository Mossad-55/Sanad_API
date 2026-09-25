using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class AdminMedicationAccessAuditConfiguration : IEntityTypeConfiguration<AdminMedicationAccessAudit>
{
    public void Configure(EntityTypeBuilder<AdminMedicationAccessAudit> builder)
    {
        builder.ToTable("admin_medication_access_audits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ActorUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.ActorAccountType).HasMaxLength(AdminMedicationAccessAudit.MaximumAccountTypeLength).HasColumnName("actor_account_type").IsRequired();
        builder.Property(x => x.Action).HasMaxLength(AdminMedicationAccessAudit.MaximumActionLength).HasColumnName("action").IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(AdminMedicationAccessAudit.MaximumResourceTypeLength).HasColumnName("resource_type").IsRequired();
        builder.Property(x => x.ResourceId).HasColumnName("resource_id");
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(AdminMedicationAccessAudit.MaximumCorrelationIdLength).HasColumnName("correlation_id").IsRequired();
        builder.HasIndex(x => x.OccurredOnUtc);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId, x.OccurredOnUtc });
    }
}
