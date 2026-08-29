using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CaregiverConfiguration :
    IEntityTypeConfiguration<Caregiver>
{
    public void Configure(EntityTypeBuilder<Caregiver> builder)
    {
        builder.ToTable("caregivers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        // Cross-module reference: scalar user_id, index, no EF relationship.
        builder.Property(c => c.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("user_id")
            .IsRequired();
        builder.HasIndex(c => c.UserId);

        builder.Property(c => c.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(c => c.StatusReason)
            .HasColumnName("status_reason")
            .HasMaxLength(500);
        builder.Property(c => c.Availability)
            .HasColumnName("availability")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(c => c.DetailedAddress)
            .HasColumnName("detailed_address")
            .HasMaxLength(Caregiver.MaximumDetailedAddressLength);

        builder.Property(c => c.AverageRating)
            .HasColumnName("average_rating")
            .HasPrecision(3, 2)
            .IsRequired();
        builder.Property(c => c.ReviewsCount)
            .HasColumnName("reviews_count")
            .IsRequired();

        builder.Property(c => c.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(c => c.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.Ignore(c => c.DomainEvents);

        // ---- Owned value objects: table-split onto caregivers (nullable) ----
        builder.OwnsOne(c => c.MedicalProfile, profile =>
        {
            profile.Property(p => p.ProfessionalTitleId)
                .HasConversion(id => id.Value, value => new ProfessionalTitleId(value))
                .HasColumnName("medical_professional_title_id");
            profile.Property(p => p.YearsOfExperience)
                .HasColumnName("medical_years_of_experience");
            profile.Property(p => p.SpecializationId)
                .HasConversion(id => id.Value, value => new SpecializationId(value))
                .HasColumnName("medical_specialization_id");
            profile.Property(p => p.AcademicDegreeId)
                .HasConversion(id => id.Value, value => new AcademicDegreeId(value))
                .HasColumnName("medical_academic_degree_id");
            profile.Property(p => p.CurrentWorkplace)
                .HasColumnName("medical_current_workplace")
                .HasMaxLength(MedicalCaregiverProfile.MaximumWorkplaceLength);
            profile.Property(p => p.Biography)
                .HasColumnName("medical_biography")
                .HasMaxLength(MedicalCaregiverProfile.MaximumBiographyLength);
        });

        builder.OwnsOne(c => c.CompanionProfile, profile =>
        {
            profile.Property(p => p.YearsOfExperience)
                .HasColumnName("companion_years_of_experience");
            profile.Property(p => p.SpecializationId)
                .HasConversion(id => id.Value, value => new SpecializationId(value))
                .HasColumnName("companion_specialization_id");
            profile.Property(p => p.Biography)
                .HasColumnName("companion_biography")
                .HasMaxLength(CompanionCaregiverProfile.MaximumBiographyLength);
        });

        builder.OwnsOne(c => c.MedicalPricing, pricing =>
        {
            pricing.Property(p => p.HomeVisitPrice)
                .HasColumnName("medical_home_visit_price")
                .HasPrecision(12, 2);
            pricing.Property(p => p.EightHourShiftPrice)
                .HasColumnName("medical_eight_hour_shift_price")
                .HasPrecision(12, 2);
            pricing.Property(p => p.TwelveHourShiftPrice)
                .HasColumnName("medical_twelve_hour_shift_price")
                .HasPrecision(12, 2);
            pricing.Property(p => p.TwentyFourHourShiftPrice)
                .HasColumnName("medical_twenty_four_hour_shift_price")
                .HasPrecision(12, 2);
        });

        builder.OwnsOne(c => c.CompanionPricing, pricing =>
        {
            pricing.Property(p => p.HourlyPrice)
                .HasColumnName("companion_hourly_price")
                .HasPrecision(12, 2);
            pricing.Property(p => p.EightHourDayPrice)
                .HasColumnName("companion_eight_hour_day_price")
                .HasPrecision(12, 2);
            pricing.Property(p => p.OvernightPrice)
                .HasColumnName("companion_overnight_price")
                .HasPrecision(12, 2);
        });

        // ---- Owned children: selections (composite key CaregiverId + LookupId) ----
        builder.OwnsMany(c => c.ServiceSelections, selection =>
        {
            selection.ToTable("caregiver_service_selections");
            selection.WithOwner().HasForeignKey("CaregiverId");
            selection.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            selection.Property(s => s.Id)
                .HasConversion(id => id.Value, value => new ServiceId(value))
                .HasColumnName("service_id")
                .IsRequired();
            selection.HasKey("CaregiverId", "Id");
        });

        builder.OwnsMany(c => c.LanguageSelections, selection =>
        {
            selection.ToTable("caregiver_language_selections");
            selection.WithOwner().HasForeignKey("CaregiverId");
            selection.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            selection.Property(s => s.Id)
                .HasConversion(id => id.Value, value => new LanguageId(value))
                .HasColumnName("language_id")
                .IsRequired();
            selection.HasKey("CaregiverId", "Id");
        });

        builder.OwnsMany(c => c.AreaSelections, selection =>
        {
            selection.ToTable("caregiver_area_selections");
            selection.WithOwner().HasForeignKey("CaregiverId");
            selection.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            selection.Property(s => s.Id)
                .HasConversion(id => id.Value, value => new AreaId(value))
                .HasColumnName("area_id")
                .IsRequired();
            selection.HasKey("CaregiverId", "Id");
        });

        // ---- Owned children: certificates (composite key CaregiverId + CertificateId) ----
        builder.OwnsMany(c => c.Certificates, certificate =>
        {
            certificate.ToTable("caregiver_certificates");
            certificate.WithOwner().HasForeignKey("CaregiverId");
            certificate.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            certificate.Property(cert => cert.Id)
                .HasConversion(id => id.Value, value => new CaregiverCertificateId(value))
                .HasColumnName("id")
                .ValueGeneratedNever()
                .IsRequired();
            certificate.Property(cert => cert.Type)
                .HasColumnName("type")
                .HasConversion<int>()
                .IsRequired();
            certificate.Property(cert => cert.FilePath)
                .HasColumnName("file_path")
                .IsRequired();
            certificate.Property(cert => cert.ExpiryDate)
                .HasColumnName("expiry_date");
            certificate.Property(cert => cert.VerificationStatus)
                .HasColumnName("verification_status")
                .HasConversion<int>()
                .IsRequired();
            certificate.Property(cert => cert.ReviewReason)
                .HasColumnName("review_reason")
                .HasMaxLength(500);
            certificate.Property(cert => cert.CreatedOnUtc)
                .HasColumnName("created_on_utc")
                .IsRequired();
            certificate.Property(cert => cert.UpdatedOnUtc)
                .HasColumnName("updated_on_utc")
                .IsRequired();
            certificate.HasKey("CaregiverId", "Id");
        });

        // ---- Owned schedules: their own tables + nested owned collections ----
        builder.OwnsOne(c => c.MedicalSchedule, schedule =>
        {
            schedule.ToTable("medical_schedules");
            schedule.WithOwner().HasForeignKey("CaregiverId");
            schedule.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            schedule.HasKey("CaregiverId");
            schedule.Ignore(s => s.HasAvailability);

            schedule.OwnsMany(s => s.Shifts, shift =>
            {
                shift.ToTable("medical_shifts");
                shift.WithOwner().HasForeignKey("MedicalScheduleId");
                shift.Property<CaregiverId>("MedicalScheduleId")
                    .HasConversion(id => id.Value, value => new CaregiverId(value))
                    .HasColumnName("medical_schedule_id")
                    .IsRequired();
                shift.Property(sh => sh.DayOfWeek)
                    .HasColumnName("day_of_week")
                    .HasConversion<int>()
                    .IsRequired();
                shift.Property(sh => sh.ShiftType)
                    .HasColumnName("shift_type")
                    .HasConversion<int>()
                    .IsRequired();
                shift.Ignore(sh => sh.StartTime);
                shift.Ignore(sh => sh.EndTime);
                shift.Ignore(sh => sh.Duration);
                shift.Ignore(sh => sh.EndsNextDay);
            });

            schedule.OwnsMany(s => s.HomeVisitWindows, window =>
            {
                window.ToTable("medical_home_visit_windows");
                window.WithOwner().HasForeignKey("MedicalScheduleId");
                window.Property<CaregiverId>("MedicalScheduleId")
                    .HasConversion(id => id.Value, value => new CaregiverId(value))
                    .HasColumnName("medical_schedule_id")
                    .IsRequired();
                window.Property(w => w.DayOfWeek)
                    .HasColumnName("day_of_week")
                    .HasConversion<int>()
                    .IsRequired();
                window.Property(w => w.StartTime)
                    .HasColumnName("start_time")
                    .IsRequired();
                window.Property(w => w.EndTime)
                    .HasColumnName("end_time")
                    .IsRequired();
                window.Ignore(w => w.Duration);
            });
        });

        builder.OwnsOne(c => c.CompanionSchedule, schedule =>
        {
            schedule.ToTable("companion_schedules");
            schedule.WithOwner().HasForeignKey("CaregiverId");
            schedule.Property<CaregiverId>("CaregiverId")
                .HasConversion(id => id.Value, value => new CaregiverId(value))
                .HasColumnName("caregiver_id")
                .IsRequired();
            schedule.HasKey("CaregiverId");
            schedule.Ignore(s => s.HasAvailability);

            schedule.OwnsMany(s => s.Windows, window =>
            {
                window.ToTable("companion_availability_windows");
                window.WithOwner().HasForeignKey("CompanionScheduleId");
                window.Property<CaregiverId>("CompanionScheduleId")
                    .HasConversion(id => id.Value, value => new CaregiverId(value))
                    .HasColumnName("companion_schedule_id")
                    .IsRequired();
                window.Property(w => w.BookingType)
                    .HasColumnName("booking_type")
                    .HasConversion<int>()
                    .IsRequired();
                window.Property(w => w.DayOfWeek)
                    .HasColumnName("day_of_week")
                    .HasConversion<int>()
                    .IsRequired();
                window.Property(w => w.StartTime)
                    .HasColumnName("start_time")
                    .IsRequired();
                window.Property(w => w.EndTime)
                    .HasColumnName("end_time")
                    .IsRequired();
                window.Ignore(w => w.EndsNextDay);
                window.Ignore(w => w.Duration);
            });
        });
    }
}