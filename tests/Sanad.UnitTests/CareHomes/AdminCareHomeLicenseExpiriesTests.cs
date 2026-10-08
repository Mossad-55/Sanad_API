using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class AdminCareHomeLicenseExpiriesTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Query_returns_latest_approved_revision_licenses_through_date_in_expiry_order()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility expired = await AddApproved(db, Today.AddDays(-1));
        Guid approvedLicenseId = expired.Documents.Single(x => x.ProfileRevisionId == expired.ApprovedRevisionId
            && x.Type == CareHomeDocumentType.OperatingLicense).Id;

        // A newly uploaded replacement is still in a draft revision and cannot hide the approved license.
        expired.SaveDraft(expired.OwnerUserId, expired.Version, CareHomeFacilityTests.Draft(), Now.AddMinutes(4));
        expired.UploadDocument(expired.OwnerUserId, CareHomeDocumentType.OperatingLicense,
            "private/pending-replacement.pdf", "application/pdf", 100, Today.AddDays(60), Now.AddMinutes(5));

        await AddApproved(db, Today);
        await AddApproved(db, Today.AddDays(1));
        await AddApproved(db, null, nonExpiring: true);
        await AddPending(db);
        await db.SaveChangesAsync();

        var handler = new GetAdminCareHomeLicenseExpiriesQueryHandler(db);
        var first = await handler.Handle(new(Today.AddDays(1), 1, 2, Now), default);
        var second = await handler.Handle(new(Today.AddDays(1), 2, 2, Now), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(3, first.Value.TotalCount);
        Assert.Collection(first.Value.Items,
            item =>
            {
                Assert.Equal(expired.Id.Value, item.FacilityId);
                Assert.Equal(approvedLicenseId, item.LicenseDocumentId);
                Assert.Equal(-1, item.DaysUntilExpiry);
                Assert.True(item.IsExpired);
            },
            item =>
            {
                Assert.Equal(Today, item.ExpiryDate);
                Assert.Equal(0, item.DaysUntilExpiry);
                Assert.False(item.IsExpired);
            });
        Assert.Single(second.Value.Items);
        Assert.Equal(Today.AddDays(1), second.Value.Items[0].ExpiryDate);
        Assert.DoesNotContain(first.Value.Items, item => item.ExpiryDate > Today.AddDays(1));
    }

    [Fact]
    public async Task Query_rejects_invalid_paging_or_missing_through_date()
    {
        await using CareHomesDbContext db = CreateDb();
        var handler = new GetAdminCareHomeLicenseExpiriesQueryHandler(db);

        var missingThrough = await handler.Handle(new(default, 1, 20, Now), default);
        var invalidPage = await handler.Handle(new(Today, 0, 20, Now), default);
        var invalidPageSize = await handler.Handle(new(Today, 1, 101, Now), default);

        Assert.Equal("CareHomes.Admin.InvalidQuery", missingThrough.Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", invalidPage.Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", invalidPageSize.Error.Code);
    }

    [Fact]
    public void Route_uses_operational_admin_policy_and_exposes_the_expiry_queue()
    {
        Type controller = typeof(AdminCareHomesController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin,
            Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("license-expirations", Assert.Single(controller.GetMethod(nameof(AdminCareHomesController.LicenseExpirations))!
            .GetCustomAttributes<HttpGetAttribute>()).Template);
    }

    private static async Task<CareHomeFacility> AddApproved(CareHomesDbContext db, DateOnly? licenseExpiry,
        bool nonExpiring = false)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{Guid.NewGuid():N}.pdf", "application/pdf", 100,
                type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null, Now.AddSeconds(1));
        facility.Submit(owner, facility.Version, Now.AddMinutes(1));
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id,
                document.Type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null,
                document.Type != CareHomeDocumentType.OperatingLicense || nonExpiring, Now.AddMinutes(2));
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(3), Today.AddDays(-5));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static async Task AddPending(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{Guid.NewGuid():N}.pdf", "application/pdf", 100,
                type == CareHomeDocumentType.OperatingLicense ? Today.AddDays(-1) : null, Now.AddSeconds(1));
        facility.Submit(owner, facility.Version, Now.AddMinutes(1));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
