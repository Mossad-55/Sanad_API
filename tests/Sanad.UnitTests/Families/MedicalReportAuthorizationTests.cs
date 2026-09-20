using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class MedicalReportAuthorizationTests
{
    [Fact]
    public async Task OwnerAndViewerCanReadMedicalFeed()
    {
        using var db = CreateDb();
        UserId owner = UserId.New(); UserId viewer = UserId.New();
        Family family = Family.Create(owner, "Family");
        family.AddMember(FamilyMember.Create(viewer, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        db.Families.Add(family);
        db.MedicalReports.Add(CreateReport(family.Id));
        await db.SaveChangesAsync();

        foreach (UserId user in new[] { owner, viewer })
        {
            var result = await new GetFamilyReportsQueryHandler(db).Handle(new GetFamilyReportsQuery(user, "medical", null, null), CancellationToken.None);
            Assert.True(result.IsSuccess); Assert.Single(result.Value.Items); Assert.Equal("medical", result.Value.Items[0].ReportType);
        }
    }

    [Fact]
    public async Task NonMemberCannotReadMedicalFeed()
    {
        using var db = CreateDb();
        var family = Family.Create(UserId.New(), "Family"); db.Families.Add(family); db.MedicalReports.Add(CreateReport(family.Id)); await db.SaveChangesAsync();
        var result = await new GetFamilyReportsQueryHandler(db).Handle(new GetFamilyReportsQuery(UserId.New(), "medical", null, null), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal("Reports.AccessDenied", result.Error.Code);
    }

    private static MedicalReport CreateReport(FamilyId familyId)
    {
        UserId caregiver = UserId.New();
        return MedicalReport.Create(BookingId.New(), familyId, ElderlyId.New(), CaregiverId.New(), caregiver, BookingCaregiverType.Medical, null, null, null, null, "Notes", null, VisitReportAssessment.NotAssessed, DateTime.UtcNow, false, DateTime.UtcNow, caregiver, null);
    }
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
