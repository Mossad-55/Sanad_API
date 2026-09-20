using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class VisitReportAuthorizationTests
{
    [Fact]
    public async Task FamilyReportQuery_UsesAllActiveMemberships()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        UserId viewer = UserId.New();
        Family first = Family.Create(UserId.New(), "First");
        first.AddMember(FamilyMember.Create(viewer, first.OwnerUserId, FamilyRelationshipType.Other, FamilyRole.Viewer));
        Family second = Family.Create(UserId.New(), "Second");
        second.AddMember(FamilyMember.Create(viewer, second.OwnerUserId, FamilyRelationshipType.Other, FamilyRole.Editor));
        dbContext.Families.AddRange(first, second);
        dbContext.VisitReports.AddRange(
            CreateReport(first.Id),
            CreateReport(second.Id));
        await dbContext.SaveChangesAsync();

        var result = await new GetFamilyVisitReportsQueryHandler(dbContext).Handle(
            new GetFamilyVisitReportsQuery(viewer, "visit", null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Contains(item.FamilyId, new[] { first.Id.Value, second.Id.Value }));
    }

    [Fact]
    public async Task FamilyReportQuery_RejectsUserWithoutActiveMembership()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        var result = await new GetFamilyVisitReportsQueryHandler(dbContext).Handle(
            new GetFamilyVisitReportsQuery(UserId.New(), "visit", null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Reports.Visit.AccessDenied", result.Error.Code);
    }

    private static VisitReport CreateReport(FamilyId familyId) => VisitReport.Create(
        BookingId.New(),
        familyId,
        ElderlyId.New(),
        CaregiverId.New(),
        UserId.New(),
        BookingCaregiverType.Companion,
        "Observed",
        null,
        null,
        VisitReportAssessment.NoImmediateConcern,
        DateTime.UtcNow.AddHours(-2),
        DateTime.UtcNow.AddHours(-1),
        DateTime.UtcNow);

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FamiliesDbContext(options);
    }
}
