using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class MedicalReportConfiguration : IEntityTypeConfiguration<MedicalReport>
{
    public void Configure(EntityTypeBuilder<MedicalReport> builder)
    {
        builder.ToTable("medical_reports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new MedicalReportId(x)).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.BookingId).HasConversion(x => x.Value, x => new BookingId(x)).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.ElderlyId).HasConversion(x => x.Value, x => new ElderlyId(x)).HasColumnName("elderly_id").IsRequired();
        builder.Property(x => x.CaregiverId).HasConversion(x => x.Value, x => new CaregiverId(x)).HasColumnName("caregiver_id").IsRequired();
        builder.Property(x => x.CaregiverUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("caregiver_user_id").IsRequired();
        builder.Property(x => x.CaregiverType).HasConversion<int>().HasColumnName("caregiver_type").IsRequired();
        builder.Property(x => x.Systolic).HasColumnName("systolic");
        builder.Property(x => x.Diastolic).HasColumnName("diastolic");
        builder.Property(x => x.Pulse).HasColumnName("pulse");
        builder.Property(x => x.Temperature).HasColumnName("temperature");
        builder.Property(x => x.Notes).HasMaxLength(2000).HasColumnName("notes");
        builder.Property(x => x.MeasurementTakenOnUtc).HasColumnName("measurement_taken_on_utc");
        builder.Property(x => x.Assessment).HasConversion<int>().HasColumnName("assessment").IsRequired();
        builder.Property(x => x.SubmittedOnUtc).HasColumnName("submitted_on_utc").IsRequired();
        builder.Property(x => x.PhotoConsentConfirmed).HasColumnName("photo_consent_confirmed").IsRequired();
        builder.Property(x => x.PhotoConsentAttestedOnUtc).HasColumnName("photo_consent_attested_on_utc").IsRequired();
        builder.Property(x => x.PhotoConsentCaregiverUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("photo_consent_caregiver_user_id").IsRequired();
        builder.Property(x => x.PhotoKey).HasMaxLength(500).HasColumnName("photo_key");
        builder.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("ux_medical_reports_booking");
        builder.HasIndex(x => new { x.FamilyId, x.SubmittedOnUtc });
        builder.HasIndex(x => x.ElderlyId);
        builder.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Elderly>().WithMany().HasForeignKey(x => x.ElderlyId).OnDelete(DeleteBehavior.Restrict);
    }
}
