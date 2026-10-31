using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class CaregiverMedicalAccessAuditConfiguration : IEntityTypeConfiguration<CaregiverMedicalAccessAudit>
{
    public void Configure(EntityTypeBuilder<CaregiverMedicalAccessAudit> builder)
    {
        builder.ToTable("caregiver_medical_access_audits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ActorUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.DependentId).HasConversion(x => x.Value, x => new ElderlyId(x)).HasColumnName("dependent_id").IsRequired();
        builder.Property(x => x.GranteeUserId).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new UserId(x.Value) : null).HasColumnName("grantee_user_id");
        builder.Property(x => x.GrantId).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new MedicalAccessGrantId(x.Value) : null).HasColumnName("grant_id");
        builder.Property(x => x.Action).HasConversion<int>().HasColumnName("action").IsRequired();
        builder.Property(x => x.Decision).HasConversion<int>().HasColumnName("decision").IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(CaregiverMedicalAccessAudit.MaximumReasonCodeLength).HasColumnName("reason_code");
        builder.Property(x => x.GrantType).HasConversion<int?>().HasColumnName("grant_type");
        builder.Property(x => x.CanViewRecords).HasColumnName("can_view_records");
        builder.Property(x => x.CanEditRecords).HasColumnName("can_edit_records");
        builder.Property(x => x.CanShareWithOthers).HasColumnName("can_share_with_others");
        builder.Property(x => x.ExpiresOnUtc).HasColumnName("expires_on_utc");
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(CaregiverMedicalAccessAudit.MaximumCorrelationIdLength).HasColumnName("correlation_id").IsRequired();
        builder.HasIndex(x => x.OccurredOnUtc);
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredOnUtc });
        builder.HasIndex(x => new { x.DependentId, x.OccurredOnUtc });
    }
}