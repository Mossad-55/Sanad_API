using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeDiscoveryTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task List_RejectsInvalidPagination(int page, int pageSize)
    {
        await using CareHomesDbContext db = CreateDb();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(Utc(12)))
            .Handle(new(page, pageSize), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Discovery.InvalidQuery", result.Error.Code);
    }

    [Fact]
    public async Task List_RejectsPageOffsetThatCannotBeRepresentedSafely()
    {
        await using CareHomesDbContext db = CreateDb();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(Utc(12)))
            .Handle(new(int.MaxValue, 50), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Discovery.InvalidQuery", result.Error.Code);
    }

    [Fact]
    public async Task List_UsesApprovedRevisionOnly_OrdersById_AndReturnsMinimumActivePriceIncludingZero()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility first = await AddApproved(db, now, "First");
        CareHomeFacility second = await AddApproved(db, now, "Second");
        await AddRoomType(db, first, 250, now);
        await AddRoomType(db, first, 0, now);
        CareHomeRoomType archived = await AddRoomType(db, first, 10, now);
        archived.Archive(now);
        await AddRoomType(db, second, 100, now);

        UserId owner = first.OwnerUserId;
        CareHomeProfileRevision approved = first.Revisions.Single(x => x.Id == first.ApprovedRevisionId);
        first.SaveDraft(owner, first.Version, CareHomeFacilityTests.Draft() with { EnglishName = "Unapproved draft" }, now.AddMinutes(1));
        await db.SaveChangesAsync();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now))
            .Handle(new(1, 10), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(new[] { first.Id.Value, second.Id.Value }.OrderBy(x => x), result.Value.Items.Select(x => x.Id));
        var firstSummary = result.Value.Items.Single(x => x.Id == first.Id.Value);
        Assert.Equal(approved.EnglishName, firstSummary.EnglishName);
        Assert.Equal(0m, firstSummary.StartingFromMonthlyPriceEgp);
    }

    [Fact]
    public async Task InMemoryProvider_OrdersByFacilityIdBeforeApplyingPagination()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility first = await AddApproved(db, now, "First page");
        CareHomeFacility second = await AddApproved(db, now, "Second page");
        CareHomeFacility third = await AddApproved(db, now, "Third page");
        Guid[] expectedIds = [first.Id.Value, second.Id.Value, third.Id.Value];
        Array.Sort(expectedIds);

        var handler = new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now));
        var page1 = await handler.Handle(new(1, 1), default);
        var page2 = await handler.Handle(new(2, 1), default);
        var page3 = await handler.Handle(new(3, 1), default);

        Assert.Equal(3, page1.Value.TotalCount);
        Assert.Equal(expectedIds[0], Assert.Single(page1.Value.Items).Id);
        Assert.Equal(expectedIds[1], Assert.Single(page2.Value.Items).Id);
        Assert.Equal(expectedIds[2], Assert.Single(page3.Value.Items).Id);
    }

    [Fact]
    public async Task InMemoryProvider_BoundedRoomPredicateKeepsPricesWithTheirFacilities()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility withActiveRoom = await AddApproved(db, now, "Active room");
        await AddRoomType(db, withActiveRoom, 123, now);
        CareHomeFacility withOnlyArchivedRoom = await AddApproved(db, now, "Archived room");
        CareHomeRoomType archived = await AddRoomType(db, withOnlyArchivedRoom, 77, now);
        archived.Archive(now);
        await db.SaveChangesAsync();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now)).Handle(new(1, 10), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(123m, result.Value.Items.Single(x => x.Id == withActiveRoom.Id.Value).StartingFromMonthlyPriceEgp);
        Assert.Null(result.Value.Items.Single(x => x.Id == withOnlyArchivedRoom.Id.Value).StartingFromMonthlyPriceEgp);
    }

    [Fact]
    public async Task List_ExcludesNonApprovedAndIncompleteFacilities_AndPaginatesInIdOrder()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility approved = await AddApproved(db, now, "Approved");
        CareHomeFacility draft = CareHomeFacility.CreateDraft(UserId.New(), now);
        draft.SaveDraft(draft.OwnerUserId, draft.Version, CareHomeFacilityTests.Draft() with { EnglishName = "Draft" }, now);
        db.Facilities.Add(draft);
        await db.SaveChangesAsync();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now))
            .Handle(new(2, 1), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(approved.Id.Value, (await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now)).Handle(new(1, 1), default)).Value.Items[0].Id);
    }

    [Fact]
    public async Task EligibleFacilityWithNoActiveRoomTypes_RemainsListedWithNullPriceAndEmptyDetailRooms()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, now, "No active rooms");
        CareHomeRoomType archived = await AddRoomType(db, facility, 75, now);
        archived.Archive(now);
        await db.SaveChangesAsync();

        var list = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now)).Handle(new(1, 10), default);
        var detail = await new GetCareHomeDiscoveryDetailQueryHandler(db, new FixedClock(now)).Handle(new(facility.Id.Value), default);

        Assert.True(list.IsSuccess);
        var summary = Assert.Single(list.Value.Items);
        Assert.Null(summary.StartingFromMonthlyPriceEgp);
        Assert.True(detail.IsSuccess);
        Assert.Empty(detail.Value.RoomTypes);
    }

    [Fact]
    public async Task LatestReplacementDocumentWins_AndRejectedOrExpiredReplacementHidesFacility()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, now, "Replacement");
        CareHomeProfileRevision revision = facility.Revisions.Single(x => x.Id == facility.ApprovedRevisionId);
        CareHomeDocument replacement = CareHomeDocument.Upload(
            CareHomeDocumentType.OperatingLicense, revision.Id, "private/replacement.pdf", "application/pdf", 10,
            CairoDate(now).AddDays(10), now.AddMinutes(1));
        replacement.Reject(UserId.New(), "Unreadable", now.AddMinutes(2));
        AddDocumentToAggregate(facility, replacement);
        await db.SaveChangesAsync();

        var result = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(now)).Handle(new(1, 10), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task ExpiryDate_IsUsableOnCairoDate_AndExpiresOnTheFollowingDate()
    {
        DateTime beforeMidnight = new(2026, 10, 3, 20, 59, 59, DateTimeKind.Utc);
        DateTime afterMidnight = new(2026, 10, 3, 21, 0, 1, DateTimeKind.Utc);
        DateOnly expiry = CairoDate(beforeMidnight);
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, beforeMidnight, "Boundary", expiry);

        var usable = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(beforeMidnight)).Handle(new(1, 10), default);
        var expired = await new GetCareHomeDiscoveryQueryHandler(db, new FixedClock(afterMidnight)).Handle(new(1, 10), default);

        Assert.Equal(1, usable.Value.TotalCount);
        Assert.Equal(0, expired.Value.TotalCount);
        Assert.Equal(facility.Id.Value, usable.Value.Items[0].Id);
    }

    [Fact]
    public async Task Detail_ReturnsPublicApprovedDataOnly_AndUnknownAndIneligibleIdsShareNotFound()
    {
        DateTime now = Utc(12);
        await using CareHomesDbContext db = CreateDb();
        CareHomeProfileDraft publicDraft = CareHomeFacilityTests.Draft() with
        {
            Address = "Public address",
            ArabicAdmissionConditions = "شروط القبول",
            EnglishAdmissionConditions = "Admission rules",
            Amenities = [new("حديقة", "Garden")],
            MedicalServices = [new("تمريض", "Nursing")]
        };
        CareHomeFacility approved = await AddApproved(db, now, "Public", draft: publicDraft);
        CareHomeRoomType active = await AddRoomType(db, approved, 125, now);
        active.Update("غرفة خاصة", "Private room", 125, CareHomeAllocationMode.Private, "Active description", "Active description", now);
        CareHomeRoomType archived = await AddRoomType(db, approved, 75, now);
        archived.Archive(now);
        await db.SaveChangesAsync();
        CareHomeFacility draft = CareHomeFacility.CreateDraft(UserId.New(), now);
        db.Facilities.Add(draft);
        await db.SaveChangesAsync();

        var handler = new GetCareHomeDiscoveryDetailQueryHandler(db, new FixedClock(now));
        var detail = await handler.Handle(new(approved.Id.Value), default);
        var unknown = await handler.Handle(new(Guid.NewGuid()), default);
        var ineligible = await handler.Handle(new(draft.Id.Value), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal("Public address", detail.Value.Address);
        Assert.Equal("شروط القبول", detail.Value.ArabicAdmissionConditions);
        Assert.Equal("Admission rules", detail.Value.EnglishAdmissionConditions);
        Assert.Equal([new BilingualCareHomeItem("حديقة", "Garden")], detail.Value.Amenities);
        Assert.Equal([new BilingualCareHomeItem("تمريض", "Nursing")], detail.Value.MedicalServices);
        var room = Assert.Single(detail.Value.RoomTypes);
        Assert.Equal(active.Id, room.Id);
        Assert.Equal("غرفة خاصة", room.ArabicName);
        Assert.Equal("Private room", room.EnglishName);
        Assert.Equal("Active description", room.ArabicDescription);
        Assert.Equal("Active description", room.EnglishDescription);
        Assert.Equal(CareHomeAllocationMode.Private, room.AllocationMode);
        Assert.Equal(125m, room.MonthlyPriceEgp);
        Assert.DoesNotContain(detail.Value.RoomTypes, x => x.Id == archived.Id);
        Assert.DoesNotContain("Contact", detail.Value.Summary.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateStorageKey", detail.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(unknown.IsSuccess);
        Assert.False(ineligible.IsSuccess);
        Assert.Equal(unknown.Error.Code, ineligible.Error.Code);
        Assert.Equal("CareHomes.Discovery.NotFound", unknown.Error.Code);
    }

    [Fact]
    public void Controller_IsAnonymous_AndDiscoveryErrorsMapToExpectedHttpStatuses()
    {
        Assert.NotEmpty(typeof(CareHomeDiscoveryController).GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal(400, ResultProblemDetailsMapper.Create(new("CareHomes.Discovery.InvalidQuery", "test"), new DefaultHttpContext()).Status);
        Assert.Equal(404, ResultProblemDetailsMapper.Create(new("CareHomes.Discovery.NotFound", "test"), new DefaultHttpContext()).Status);
    }

    private static async Task<CareHomeFacility> AddApproved(CareHomesDbContext db, DateTime now, string name, DateOnly? expiry = null, CareHomeProfileDraft? draft = null)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, (draft ?? CareHomeFacilityTests.Draft()) with { EnglishName = name }, now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{name}-{type}.pdf", "application/pdf", 10, expiry, now);
        facility.Submit(owner, facility.Version, now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, expiry, expiry is null, now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now, CairoDate(now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static async Task<CareHomeRoomType> AddRoomType(CareHomesDbContext db, CareHomeFacility facility, decimal price, DateTime now)
    {
        CareHomeRoomType roomType = CareHomeRoomType.Create(facility.Id, "غرفة", "Room", price, CareHomeAllocationMode.Private, now);
        db.RoomTypes.Add(roomType);
        await db.SaveChangesAsync();
        return roomType;
    }

    private static void AddDocumentToAggregate(CareHomeFacility facility, CareHomeDocument document)
    {
        FieldInfo field = typeof(CareHomeFacility).GetField("_documents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((List<CareHomeDocument>)field.GetValue(facility)!).Add(document);
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DateTime Utc(int hour) => new(2026, 10, 3, hour, 0, 0, DateTimeKind.Utc);
    private static DateOnly CairoDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, CairoZone()));
    private static TimeZoneInfo CairoZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
    }

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
