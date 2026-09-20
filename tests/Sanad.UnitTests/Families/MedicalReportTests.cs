using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.UnitTests.Families;

public sealed class MedicalReportTests
{
    [Fact]
    public void MissingBloodPressurePairIsRejected()
    {
        Assert.Throws<DomainException>(() => MedicalReport.Create(BookingId.New(), FamilyId.New(), ElderlyId.New(), CaregiverId.New(), UserId.New(), BookingCaregiverType.Medical, 120, null, null, null, null, null, VisitReportAssessment.NoImmediateConcern, DateTime.UtcNow, false, DateTime.UtcNow, UserId.New(), null));
    }

    [Fact]
    public void MissingMeasurementTimeIsRejected()
    {
        Assert.Throws<DomainException>(() => MedicalReport.Create(BookingId.New(), FamilyId.New(), ElderlyId.New(), CaregiverId.New(), UserId.New(), BookingCaregiverType.Medical, null, null, 80, null, null, null, VisitReportAssessment.NoImmediateConcern, DateTime.UtcNow, false, DateTime.UtcNow, UserId.New(), null));
    }

    [Fact]
    public void NotesAreTrimmedAndLimited()
    {
        UserId caregiverUserId = UserId.New();
        var report = MedicalReport.Create(BookingId.New(), FamilyId.New(), ElderlyId.New(), CaregiverId.New(), caregiverUserId, BookingCaregiverType.Medical, null, null, null, null, "  Follow up  ", null, VisitReportAssessment.NotAssessed, DateTime.UtcNow, false, DateTime.UtcNow, caregiverUserId, null);
        Assert.Equal("Follow up", report.Notes);
        Assert.Throws<DomainException>(() => MedicalReport.Create(BookingId.New(), FamilyId.New(), ElderlyId.New(), CaregiverId.New(), caregiverUserId, BookingCaregiverType.Medical, null, null, null, null, new string('x', 2001), null, VisitReportAssessment.NotAssessed, DateTime.UtcNow, false, DateTime.UtcNow, caregiverUserId, null));
    }
}
