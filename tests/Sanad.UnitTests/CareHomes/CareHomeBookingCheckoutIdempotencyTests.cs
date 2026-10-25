using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Idempotency;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.UnitTests.Families;
using Sanad.UnitTests.Finance;
using Sanad.UnitTests.Support;
using IdempotencyRecord = Sanad.Modules.CareHomes.Domain.Idempotency.IdempotencyRequest;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// Care-home checkout retry safety: the replay lookup runs inside the
/// reservation guard, so a confirmed retry answers the stored body without a
/// second booking or a second 15-minute hold, and a changed fingerprint is a
/// 409. An absent header stores no key.
/// </summary>
public sealed class CareHomeBookingCheckoutIdempotencyTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 10, 5);
    // The idempotency identity is (actor, operation, key), so retries must send
    // the same actor; a fresh UserId per call would never replay.
    private static readonly UserId Actor = UserId.New();

    private sealed class InlineReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(
            CareHomeId facilityId,
            Guid roomTypeId,
            DateOnly startDate,
            Func<Task<T>> action,
            CancellationToken cancellationToken) =>
            action();
    }

    private sealed class EmptyOccupancyProvider : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(
            CareHomeId facilityId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CareHomeOccupancyInterval>>([]);
    }

    private sealed class EffectiveCharges : IPlatformChargeRuleReader
    {
        public Task<PlatformChargeRuleRates?> GetEffectiveAsync(
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlatformChargeRuleRates?>(new(1m, 2m, 1));

        public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    }

    private static CareHomesDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(CareHomesDbContext Db, CareHomeFacility Facility, CareHomeRoomType RoomType)> SeedAsync()
    {
        var db = CreateDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(
            facility.Id,
            "Room",
            "Room",
            12000m,
            CareHomeAllocationMode.Private,
            Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(
            type.Id,
            CareHomeRoomTypeMediaKind.Cover,
            0,
            "private/care-home-media/cover.jpg",
            "image/jpeg",
            10,
            Now));
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(
            type.Id,
            CareHomeRoomTypeMediaKind.Gallery,
            0,
            "private/care-home-media/gallery.jpg",
            "image/jpeg",
            10,
            Now));
        await db.SaveChangesAsync();

        return (db, facility, type);
    }

    private static CheckoutHandler Handler(ICareHomesDbContext db) =>
        new(
            db,
            new EffectiveCharges(),
            new EmptyOccupancyProvider(),
            new InlineReservationGuard(),
            FixedPlatformControls.Operational(),
            new StubIdempotentResponseSnapshotter());

    private static CheckoutCareHomeBookingCommand Command(
        CareHomeFacility facility,
        CareHomeRoomType roomType,
        IdempotencyRequest? idempotency) =>
        new(
            Actor,
            FamilyId.New(),
            Intake(),
            facility.Id.Value,
            roomType.Id,
            StartDate,
            Now,
            idempotency);

    private static IdempotencyRequest Request(string key, string fingerprint) =>
        new(new IdempotencyKey(key), fingerprint);

    private static ElderlyIntakeResolution Intake() => new(
        FamilyId.New(),
        ElderlyId.New(),
        UserId.New(),
        "عربي",
        "Elderly",
        75,
        new ElderlyIntakeMedicalProjection("Unknown", null, null, [], [], [], null),
        new ElderlyIntakeResponsibleContact("Contact", "+201000000000", null),
        null);

    private static CareHomeFacility AddApprovedFacility(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);

        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
        {
            facility.UploadDocument(
                owner,
                documentType,
                $"private/{documentType}.pdf",
                "application/pdf",
                10,
                null,
                Now);
        }

        facility.Submit(owner, facility.Version, Now);

        foreach (CareHomeDocument document in facility.Documents.ToArray())
        {
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        }

        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, new DateOnly(2026, 10, 3));
        db.Facilities.Add(facility);

        return facility;
    }

    [Fact]
    public async Task FirstCall_WithAKey_StoresTheSnapshotWithTheSingleBookingAndHold()
    {
        var (db, facility, roomType) = await SeedAsync();
        var request = Request("care-home-key-1", "care-home-fp-1");

        var result = await Handler(db).Handle(
            Command(facility, roomType, request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Equal(200, result.Value.StatusCode);

        CareHomeBooking booking = Assert.Single(db.Bookings);
        Assert.Equal(Now.AddMinutes(15), booking.CheckoutHoldUntilUtc);

        IdempotencyRecord stored = Assert.Single(db.IdempotencyRequests);
        Assert.Equal(IdempotencyOperations.FamilyCareHomeBookingCheckout, stored.Operation);
        Assert.Equal("care-home-key-1", stored.Key);
        Assert.Equal("care-home-fp-1", stored.Fingerprint);
        Assert.Equal((Guid?)booking.Id, stored.ResourceId);
        Assert.Equal(200, stored.ResponseStatus);
        Assert.Equal(
            new StubIdempotentResponseSnapshotter().Snapshot(result.Value.Value),
            stored.ResponseBody);
    }

    [Fact]
    public async Task Retry_InsideTheGuard_ReplaysWithoutASecondBookingOrHold()
    {
        var (db, facility, roomType) = await SeedAsync();
        var request = Request("care-home-key-2", "care-home-fp-2");
        var handler = Handler(db);

        var first = await handler.Handle(
            Command(facility, roomType, request),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            Command(facility, roomType, request),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsReplay);
        Assert.Equal(200, second.Value.StatusCode);
        Assert.Equal(Assert.Single(db.IdempotencyRequests).ResponseBody, second.Value.ReplayBody);
        Assert.Single(db.Bookings);
        Assert.Single(db.IdempotencyRequests);
    }

    [Fact]
    public async Task Retry_WithAChangedFingerprint_IsA409()
    {
        var (db, facility, roomType) = await SeedAsync();
        var handler = Handler(db);

        var first = await handler.Handle(
            Command(facility, roomType, Request("care-home-key-3", "care-home-fp-3")),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            Command(facility, roomType, Request("care-home-key-3", "care-home-fp-different")),
            CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("CareHomes.Bookings.IdempotencyConflict", second.Error.Code);
        Assert.Single(db.Bookings);
        Assert.Single(db.IdempotencyRequests);
    }

    [Fact]
    public async Task AbsentHeader_KeepsTodaysBehaviorAndStoresNoKey()
    {
        var (db, facility, roomType) = await SeedAsync();

        var result = await Handler(db).Handle(
            Command(facility, roomType, idempotency: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Single(db.Bookings);
        Assert.Empty(db.IdempotencyRequests);
    }
}
