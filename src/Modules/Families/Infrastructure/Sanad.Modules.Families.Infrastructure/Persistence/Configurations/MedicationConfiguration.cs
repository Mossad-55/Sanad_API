using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("medications");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasConversion(id => id.Value, value => new MedicationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(m => m.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(m => m.CreatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(m => m.Name)
            .HasMaxLength(Medication.MaximumNameLength)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(m => m.Dosage)
            .HasMaxLength(Medication.MaximumDosageLength)
            .HasColumnName("dosage")
            .IsRequired();

        builder.Property(m => m.DoseUnit)
            .HasMaxLength(Medication.MaximumDoseUnitLength)
            .HasColumnName("dose_unit")
            .IsRequired();

        builder.Property(m => m.DoseQuantity)
            .HasColumnName("dose_quantity")
            .IsRequired();

        builder.Property(m => m.DoseTimes)
            .HasColumnName("dose_times")
            .HasColumnType("time without time zone[]")
            .IsRequired();

        builder.Property(m => m.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(m => m.EndDate)
            .HasColumnName("end_date");

        builder.Property(m => m.Instructions)
            .HasMaxLength(Medication.MaximumInstructionsLength)
            .HasColumnName("instructions");

        builder.Property(m => m.StockQuantity)
            .HasColumnName("stock_quantity");

        builder.Property(m => m.LowStockThreshold)
            .HasColumnName("low_stock_threshold");

        builder.Property(m => m.Status)
            .HasConversion<int>()
            .HasColumnName("status")
            .IsRequired();

        builder.Property(m => m.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(m => m.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(m => m.ElderlyId);
        builder.HasIndex(m => m.Status);

        builder.Ignore(m => m.DomainEvents);
    }
}