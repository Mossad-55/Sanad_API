using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeFinanceReadModelTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Successful_late_payment_keeps_capture_timestamp_before_refund()
    {
        var booking = CreateBooking();
        booking.MarkPaid(11, Now.AddMinutes(16));

        Assert.Equal(CareHomeBookingPaymentStatus.Paid, booking.PaymentStatus);
        Assert.Equal(Now.AddMinutes(16), booking.PaymentCompletedOnUtc);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
    }

    [Fact]
    public async Task Receipt_preserves_original_snapshot_and_reports_only_completed_refund()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var booking = CreateBooking(facility.Id);
        booking.MarkPaid(12, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        booking.MarkRefundManuallyCompleted("REF-1", "Confirmed externally", UserId.New(), Now.AddMinutes(3));
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new GetOwnerCareHomeReceiptHandler(db)
            .Handle(new GetOwnerCareHomeReceiptQuery(owner, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1000m, result.Value.BaseAmount);
        Assert.Equal(100m, result.Value.PlatformFeeAmount);
        Assert.Equal(50m, result.Value.TaxAmount);
        Assert.Equal(1150m, result.Value.OriginalTotal);
        Assert.Equal(1150m, result.Value.CompletedRefundAmount);
        Assert.Equal(0m, result.Value.NetCollected);
        Assert.Equal("REF-1", result.Value.RefundReference);
    }

    [Fact]
    public async Task Owner_receipt_and_notes_do_not_cross_facility_scope()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var otherFacility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        var booking = CreateBooking(otherFacility.Id);
        booking.MarkPaid(13, Now.AddMinutes(1));
        db.Facilities.AddRange(facility, otherFacility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var receipt = await new GetOwnerCareHomeReceiptHandler(db)
            .Handle(new GetOwnerCareHomeReceiptQuery(owner, booking.Id), default);
        var notes = await new AddOwnerCareHomeInternalBookingNoteHandler(db)
            .Handle(new AddOwnerCareHomeInternalBookingNoteCommand(owner, booking.Id, "private", Now), default);

        Assert.False(receipt.IsSuccess);
        Assert.False(notes.IsSuccess);
        Assert.Empty(db.InternalBookingNotes);
    }

    [Fact]
    public async Task Revenue_uses_payment_and_refund_event_dates_and_keeps_historical_rows_unattributed()
    {
        await using var db = CreateDb();
        var facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        var paid = CreateBooking(facility.Id);
        paid.MarkPaid(14, Now);
        paid.CancelByFamily(Now.AddMinutes(1));
        paid.MarkRefundManuallyCompleted("REF-2", "Confirmed", UserId.New(), Now.AddDays(1));
        var historic = CreateBooking(facility.Id);
        historic.MarkPaid(15, Now.AddDays(-30));
        db.Facilities.Add(facility);
        db.Bookings.AddRange(paid, historic);
        await db.SaveChangesAsync();
        db.Entry(historic).Property(x => x.PaymentCompletedOnUtc).CurrentValue = null;
        await db.SaveChangesAsync();

        var dayOne = await CareHomeRevenueProjection.Revenue(db, facility.Id.Value,
            new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8), default);
        var dayTwo = await CareHomeRevenueProjection.Revenue(db, facility.Id.Value,
            new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9), default);
        var all = await CareHomeRevenueProjection.Revenue(db, facility.Id.Value, null, null, default);

        Assert.True(dayOne.IsSuccess);
        Assert.Equal(1150m, dayOne.Value.Totals.GrossCollected);
        Assert.Equal(0m, dayOne.Value.Totals.CompletedRefunds);
        Assert.Equal(1150m, dayOne.Value.Totals.NetCollected);
        Assert.True(dayTwo.IsSuccess);
        Assert.Equal(0m, dayTwo.Value.Totals.GrossCollected);
        Assert.Equal(1150m, dayTwo.Value.Totals.CompletedRefunds);
        Assert.Equal(-1150m, dayTwo.Value.Totals.NetCollected);
        Assert.Contains(all.Value.Transactions, x => x.BookingId == historic.Id && x.PaymentCompletedOnUtc is null);
        Assert.Equal(2300m, all.Value.Totals.GrossCollected);
        Assert.Equal(1150m, all.Value.Totals.NetCollected);
        Assert.DoesNotContain(dayOne.Value.Transactions, x => x.BookingId == historic.Id);
    }

    [Fact]
    public async Task Notes_are_append_only_and_record_author_and_utc_time()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var booking = CreateBooking(facility.Id);
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var command = new AddOwnerCareHomeInternalBookingNoteHandler(db);
        var first = await command.Handle(new(owner, booking.Id, "Initial note", Now), default);
        var correction = await command.Handle(new(owner, booking.Id, "Correction", Now.AddMinutes(1)), default);
        var list = await new GetOwnerCareHomeInternalBookingNotesHandler(db)
            .Handle(new(owner, booking.Id), default);

        Assert.True(first.IsSuccess);
        Assert.True(correction.IsSuccess);
        Assert.NotEqual(first.Value.Id, correction.Value.Id);
        Assert.Equal(owner.Value, first.Value.AuthorUserId);
        Assert.Equal(Now, first.Value.CreatedOnUtc);
        Assert.Equal(new[] { "Initial note", "Correction" }, list.Value.Select(x => x.Text));
    }

    [Fact]
    public async Task Dashboard_counts_shared_beds_and_private_rooms_as_capacity_resources()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var shared = CareHomeRoomType.Create(facility.Id, "مشترك", "Shared", 1000m, CareHomeAllocationMode.Shared, Now);
        var privateType = CareHomeRoomType.Create(facility.Id, "خاص", "Private", 2000m, CareHomeAllocationMode.Private, Now);
        var sharedRoom = CareHomeRoom.Create(facility.Id, shared.Id, "S1", Now);
        var privateRoom = CareHomeRoom.Create(facility.Id, privateType.Id, "P1", Now);
        var availablePrivateRoom = CareHomeRoom.Create(facility.Id, privateType.Id, "P2", Now);
        var bedA = CareHomeBed.Create(facility.Id, sharedRoom.Id, "A", Now);
        var bedB = CareHomeBed.Create(facility.Id, sharedRoom.Id, "B", Now);
        var privateBed = CareHomeBed.Create(facility.Id, privateRoom.Id, "P", Now);
        var availablePrivateBed = CareHomeBed.Create(facility.Id, availablePrivateRoom.Id, "P2", Now);
        db.Facilities.Add(facility);
        db.RoomTypes.AddRange(shared, privateType);
        db.Rooms.AddRange(sharedRoom, privateRoom, availablePrivateRoom);
        db.Beds.AddRange(bedA, bedB, privateBed, availablePrivateBed);
        db.Bookings.Add(CreateBooking(facility.Id));
        var awaitingDecision = CreateBooking(facility.Id);
        awaitingDecision.MarkPaid(16, Now);
        db.Bookings.Add(awaitingDecision);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, CareHomeRevenueProjection.CairoZone()));
        var occupancy = new FixedOccupancyProvider(
        [
            new(bedA.Id, CareHomeResourceKind.Bed, today, today.AddDays(1)),
            new(privateRoom.Id, CareHomeResourceKind.Room, today, today.AddDays(1)),
            new(shared.Id, CareHomeResourceKind.RoomType, today, today.AddDays(1))
        ]);

        var result = await new GetOwnerCareHomeDashboardHandler(db, occupancy)
            .Handle(new(owner, null, null, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.OccupiedResources);
        Assert.Equal(4, result.Value.TotalResources);
        Assert.Equal(75m, result.Value.OccupancyPercentage);
        Assert.Equal(1, result.Value.PendingPaymentCount);
        Assert.Equal(1, result.Value.AwaitingDecisionCount);
    }

    [Fact]
    public void Revenue_date_bounds_are_Cairo_local_inclusive_calendar_dates()
    {
        var day = new DateOnly(2026, 10, 8);
        var range = CareHomeRevenueProjection.ToUtcRange(day, day);
        var zone = CareHomeRevenueProjection.CairoZone();
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone), range.FromUtc);
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone), range.UntilUtc);
        Assert.Throws<ArgumentException>(() => CareHomeRevenueProjection.ToUtcRange(day.AddDays(1), day));
    }

    [Fact]
    public void Csv_escapes_user_supplied_formulas_and_keeps_amounts_numeric()
    {
        var row = new CareHomeRevenueTransaction(Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 1),
            new(2026, 11, 1), Now, 17, 0m, Now.AddDays(1), "=HYPERLINK(\"bad\")", 1150m,
            -1150m, "EGP", CareHomeBookingStatus.Refunded, CareHomeBookingPaymentStatus.Refunded,
            CareHomeRefundStatus.Completed);
        var report = new CareHomeRevenueReport(null, null,
            new CareHomeRevenueTotals(0m, 0m, 0m, 0m, 1150m, -1150m, 0, 1), [row]);

        string csv = System.Text.Encoding.UTF8.GetString(CareHomeRevenueCsv.Render(report));

        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\"", csv);
        Assert.Contains(",1150.00,-1150.00,", csv);
    }

    private static CareHomeBooking CreateBooking(CareHomeId? facilityId = null) =>
        CareHomeBooking.Create(facilityId ?? CareHomeId.New(), UserId.New(), FamilyId.New(), ElderlyId.New(),
            Guid.NewGuid(), new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 4,
            "رعاية", "Care", 75, null, "Contact", null, null, null, Now);

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedOccupancyProvider(IReadOnlyList<CareHomeOccupancyInterval> intervals) : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken) =>
            Task.FromResult(intervals);
    }
}
