using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class CareRecordConfiguration : IEntityTypeConfiguration<CareRecord>
{
    public void Configure(EntityTypeBuilder<CareRecord> builder)
    {
        builder.ToTable("care_records");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(r => r.Source)
            .HasConversion<int>()
            .HasColumnName("source")
            .IsRequired();

        builder.Property(r => r.AuthorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("author_user_id")
            .IsRequired();

        builder.Property(r => r.AuthorFamilyId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new FamilyId(value.Value) : null)
            .HasColumnName("author_family_id");

        // Cross-module facility identity (CareHomes module), stored opaque —
        // the same convention as Booking.BookingAreaId.
        builder.Property(r => r.AuthorFacilityId)
            .HasColumnName("author_facility_id");

        builder.Property(r => r.AuthorCaregiverId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CaregiverId(value.Value) : null)
            .HasColumnName("author_caregiver_id");

        builder.Property(r => r.HospitalName)
            .HasMaxLength(CareRecord.MaximumHospitalNameLength)
            .HasColumnName("hospital_name")
            .IsRequired();

        builder.Property(r => r.VisitDate)
            .HasColumnName("visit_date")
            .IsRequired();

        builder.Property(r => r.Details)
            .HasMaxLength(CareRecord.MaximumDetailsLength)
            .HasColumnName("details")
            .IsRequired();

        // House optimistic-concurrency pattern (Booking.RefundAttemptVersion,
        // FamilySubscription.LifecycleVersion): a plain counter compared at
        // the database boundary, so a stale editor's commit fails loudly.
        builder.Property(r => r.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(r => r.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(r => r.UpdatedOnUtc)
            .HasColumnName("updated_on_utc");

        builder.HasIndex(r => r.ElderlyId);
        builder.HasIndex(r => new { r.ElderlyId, r.Source });
        builder.HasIndex(r => r.VisitDate);
        builder.HasIndex(r => r.AuthorFacilityId);

        // Conventional referential integrity, mirroring the house pattern in
        // MedicalReportConfiguration: an explicit FK to the dependent with
        // DeleteBehavior.Restrict — care records are medical data and must
        // never cascade-delete with a dependent (or anything else).
        builder.HasOne<Elderly>()
            .WithMany()
            .HasForeignKey(r => r.ElderlyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(r => r.DomainEvents);
    }
}
