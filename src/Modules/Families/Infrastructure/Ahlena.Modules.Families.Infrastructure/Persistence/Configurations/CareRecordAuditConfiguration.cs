using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.CareRecords;

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class CareRecordAuditConfiguration : IEntityTypeConfiguration<CareRecordAudit>
{
    public void Configure(EntityTypeBuilder<CareRecordAudit> builder)
    {
        builder.ToTable("care_record_audits");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.RecordId)
            .HasColumnName("record_id");

        builder.Property(a => a.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(a => a.Source)
            .HasConversion<int>()
            .HasColumnName("source");

        builder.Property(a => a.ActorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(a => a.Action)
            .HasConversion<int>()
            .HasColumnName("action")
            .IsRequired();

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(CareRecordAudit.MaximumCorrelationIdLength)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(a => a.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();

        builder.HasIndex(a => new { a.ElderlyId, a.OccurredOnUtc });
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredOnUtc });
        builder.HasIndex(a => a.RecordId);
        builder.HasIndex(a => a.OccurredOnUtc);
    }
}
