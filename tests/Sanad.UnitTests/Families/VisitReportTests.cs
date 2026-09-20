using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.UnitTests.Families;

public sealed class VisitReportTests
{
    [Fact]
    public void Create_TrimsOptionalText_AndKeepsAttendanceSnapshot()
    {
        DateTime started = DateTime.SpecifyKind(new DateTime(2026, 9, 20, 10, 0, 0), DateTimeKind.Utc);
        DateTime completed = started.AddHours(2);
        DateTime submitted = completed.AddMinutes(1);

        VisitReport report = VisitReport.Create(
            BookingId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            UserId.New(),
            BookingCaregiverType.Medical,
            "  Alert and responsive. ",
            null,
            "  Follow-up requested. ",
            VisitReportAssessment.NeedsFollowUp,
            started,
            completed,
            submitted);

        Assert.Equal("Alert and responsive.", report.ObservedCondition);
        Assert.Equal("Follow-up requested.", report.Notes);
        Assert.Equal(started, report.StartedOnUtc);
        Assert.Equal(completed, report.CompletedOnUtc);
        Assert.Equal(submitted, report.SubmittedOnUtc);
    }

    [Fact]
    public void Create_RejectsBlankContent()
    {
        Assert.Throws<DomainException>(() => VisitReport.Create(
            BookingId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            UserId.New(),
            BookingCaregiverType.Companion,
            "  ",
            null,
            null,
            VisitReportAssessment.NotAssessed,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsInvalidAssessment()
    {
        Assert.Throws<DomainException>(() => VisitReport.Create(
            BookingId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            UserId.New(),
            BookingCaregiverType.Medical,
            "Observed",
            null,
            null,
            (VisitReportAssessment)99,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsTextLongerThanMaximum()
    {
        Assert.Throws<DomainException>(() => VisitReport.Create(
            BookingId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            UserId.New(),
            BookingCaregiverType.Medical,
            new string('x', VisitReport.MaximumTextLength + 1),
            null,
            null,
            VisitReportAssessment.NotAssessed,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsSubmissionBeforeAttendanceCompletion()
    {
        DateTime started = DateTime.UtcNow.AddHours(-2);
        DateTime completed = started.AddHours(1);

        Assert.Throws<DomainException>(() => VisitReport.Create(
            BookingId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            UserId.New(),
            BookingCaregiverType.Companion,
            "Observed",
            null,
            null,
            VisitReportAssessment.NoImmediateConcern,
            started,
            completed,
            started));
    }
}
