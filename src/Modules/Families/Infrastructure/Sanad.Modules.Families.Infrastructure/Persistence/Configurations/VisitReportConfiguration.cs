using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class VisitReportConfiguration : IEntityTypeConfiguration<VisitReport>
{
    public void Configure(EntityTypeBuilder<VisitReport> builder)
    {
        builder.ToTable("visit_reports");

        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id)
            .HasConversion(id => id.Value, value => new VisitReportId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(report => report.BookingId)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .HasColumnName("booking_id")
            .IsRequired();
        builder.Property(report => report.FamilyId)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();
        builder.Property(report => report.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();
        builder.Property(report => report.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.Property(report => report.CaregiverUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("caregiver_user_id")
            .IsRequired();
        builder.Property(report => report.CaregiverType)
            .HasConversion<int>()
            .HasColumnName("caregiver_type")
            .IsRequired();
        builder.Property(report => report.Assessment)
            .HasConversion<int>()
            .HasColumnName("assessment")
            .IsRequired();
        builder.Property(report => report.ObservedCondition)
            .HasMaxLength(VisitReport.MaximumTextLength)
            .HasColumnName("observed_condition");
        builder.Property(report => report.Activities)
            .HasMaxLength(VisitReport.MaximumTextLength)
            .HasColumnName("activities");
        builder.Property(report => report.Notes)
            .HasMaxLength(VisitReport.MaximumTextLength)
            .HasColumnName("notes");
        builder.Property(report => report.StartedOnUtc)
            .HasColumnName("started_on_utc")
            .IsRequired();
        builder.Property(report => report.CompletedOnUtc)
            .HasColumnName("completed_on_utc")
            .IsRequired();
        builder.Property(report => report.SubmittedOnUtc)
            .HasColumnName("submitted_on_utc")
            .IsRequired();

        builder.HasIndex(report => report.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_visit_reports_booking");
        builder.HasIndex(report => new { report.FamilyId, report.SubmittedOnUtc });
        builder.HasIndex(report => report.ElderlyId);

        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(report => report.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(report => report.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Elderly>()
            .WithMany()
            .HasForeignKey(report => report.ElderlyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
