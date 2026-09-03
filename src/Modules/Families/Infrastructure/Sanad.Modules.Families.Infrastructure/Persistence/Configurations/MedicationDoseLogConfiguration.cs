using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class MedicationDoseLogConfiguration : IEntityTypeConfiguration<MedicationDoseLog>
{
    public void Configure(EntityTypeBuilder<MedicationDoseLog> builder)
    {
        builder.ToTable("medication_dose_logs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new MedicationDoseLogId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(l => l.MedicationId)
            .HasConversion(id => id.Value, value => new MedicationId(value))
            .HasColumnName("medication_id")
            .IsRequired();

        builder.Property(l => l.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(l => l.ScheduledDate)
            .HasColumnName("scheduled_date")
            .IsRequired();

        builder.Property(l => l.ScheduledTime)
            .HasColumnName("scheduled_time")
            .IsRequired();

        builder.Property(l => l.Status)
            .HasConversion<int>()
            .HasColumnName("status")
            .IsRequired();

        builder.Property(l => l.TakenAtUtc)
            .HasColumnName("taken_at_utc");

        builder.Property(l => l.SkippedAtUtc)
            .HasColumnName("skipped_at_utc");

        builder.Property(l => l.Notes)
            .HasMaxLength(MedicationDoseLog.MaximumNotesLength)
            .HasColumnName("notes");

        builder.Property(l => l.LoggedByUserId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("logged_by_user_id");

        builder.Property(l => l.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(l => l.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(l => new { l.ElderlyId, l.ScheduledDate });
        builder.HasIndex(l => l.MedicationId);
        builder.HasIndex(l => l.Status);
    }
}