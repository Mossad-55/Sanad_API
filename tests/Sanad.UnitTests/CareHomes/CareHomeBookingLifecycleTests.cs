using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Application.Results;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeBookingLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_snapshots_month_price_contacts_and_fifteen_minute_checkout_hold()
    {
        var booking = Create(start: new DateOnly(2026, 10, 5), baseAmount: 12000m, fee: 600m, tax: 630m);

        Assert.Equal(new DateOnly(2026, 10, 5), booking.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 5), booking.EndDate);
        Assert.Equal(13230m, booking.TotalAmount);
        Assert.Equal(Now.AddMinutes(15), booking.CheckoutHoldUntilUtc);
        Assert.Equal(Now.AddHours(24), booking.EarliestArrivalUtc);
        Assert.Equal(CareHomeBookingStatus.PendingPayment, booking.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Pending, booking.PaymentStatus);
        Assert.StartsWith("chb_", booking.MerchantReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checkout_handler_uses_shortened_development_checkout_hold_and_keeps_arrival_at_24_hours()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Room", "Room", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var result = await new CheckoutHandler(
                db,
                new EffectiveCharges(),
                new EmptyCareHomeOccupancyProvider(),
                new InlineReservationGuard(),
                new CareHomeBookingTiming(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5)))
            .Handle(new CheckoutCareHomeBookingCommand(
                UserId.New(), FamilyId.New(), Intake(), facility.Id.Value, type.Id,
                new DateOnly(2026, 10, 5), Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddSeconds(30), result.Value.HoldUntilUtc);
        Assert.Equal(Now.AddHours(24), result.Value.EarliestArrivalUtc);
    }

    [Fact]
    public async Task Checkout_rejects_facility_with_expired_latest_operating_license_without_creating_booking()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db, new DateOnly(2026, 10, 2));
        CareHomeRoomType type = await AddBookableRoomType(db, facility);

        var result = await CreateCheckoutHandler(db).Handle(new CheckoutCareHomeBookingCommand(
            UserId.New(), FamilyId.New(), Intake(), facility.Id.Value, type.Id,
            new DateOnly(2026, 10, 5), Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.NotFound", result.Error.Code);
        Assert.Empty(db.Bookings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-03")]
    public async Task Checkout_accepts_current_or_verified_non_expiring_operating_license(string? expiryText)
    {
        await using var db = CreateBookingDb();
        DateOnly? expiry = expiryText is null ? null : DateOnly.Parse(expiryText);
        CareHomeFacility facility = AddApprovedFacility(db, expiry);
        CareHomeRoomType type = await AddBookableRoomType(db, facility);

        var result = await CreateCheckoutHandler(db).Handle(new CheckoutCareHomeBookingCommand(
            UserId.New(), FamilyId.New(), Intake(), facility.Id.Value, type.Id,
            new DateOnly(2026, 10, 5), Now), default);

        Assert.True(result.IsSuccess);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task Confirm_payment_handler_uses_shortened_development_decision_hold()
    {
        await using var db = CreateBookingDb();
        var booking = Create(new DateOnly(2026, 10, 5));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var timing = new CareHomeBookingTiming(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

        var result = await new ConfirmPaymentHandler(db, new FakePaymob(), timing)
            .Handle(new ConfirmCareHomePaymentCommand(
                booking.MerchantReference,
                7001,
                decimal.ToInt64(booking.TotalAmount * 100m),
                true,
                false,
                Now.AddSeconds(1)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddSeconds(1).AddMinutes(1), booking.DecisionHoldUntilUtc);
        Assert.Equal(CareHomeBookingStatus.PaidAwaitingDecision, booking.Status);
    }

    [Fact]
    public async Task Successful_callback_retries_after_a_concurrent_failure_and_refunds_the_late_payment()
    {
        string databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;
        var booking = Create(new DateOnly(2026, 10, 5));
        await using (var seed = new CareHomesDbContext(options))
        {
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        await using var db = new CareHomesDbContext(options);
        var competing = new ConcurrentSaveCareHomesDbContext(db, async () =>
        {
            await using var winner = new CareHomesDbContext(options);
            var current = await winner.Bookings.SingleAsync(x => x.Id == booking.Id);
            current.MarkPaymentFailed(Now.AddMinutes(1));
            await winner.SaveChangesAsync();
        });
        var paymob = new FakePaymob();

        var result = await new ConfirmPaymentHandler(competing, paymob).Handle(
            new ConfirmCareHomePaymentCommand(booking.MerchantReference, 8101,
                decimal.ToInt64(booking.TotalAmount * 100m), true, false, Now.AddMinutes(2)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, paymob.RefundCalls);
        await using var verify = new CareHomesDbContext(options);
        var persisted = await verify.Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, persisted.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.RefundInitiated, persisted.PaymentStatus);
        Assert.Equal(8101, persisted.PaymobTransactionId);
    }

    [Fact]
    public async Task Duplicate_late_success_after_concurrent_commit_still_submits_pending_refund()
    {
        string databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;
        var booking = Create(new DateOnly(2026, 10, 5));
        await using (var seed = new CareHomesDbContext(options))
        {
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        await using var db = new CareHomesDbContext(options);
        var competing = new ConcurrentSaveCareHomesDbContext(db, async () =>
        {
            await using var winner = new CareHomesDbContext(options);
            var current = await winner.Bookings.SingleAsync(x => x.Id == booking.Id);
            current.MarkPaid(8103, Now.AddMinutes(16));
            await winner.SaveChangesAsync();
        });
        var paymob = new FakePaymob();

        var result = await new ConfirmPaymentHandler(competing, paymob).Handle(
            new ConfirmCareHomePaymentCommand(booking.MerchantReference, 8103,
                decimal.ToInt64(booking.TotalAmount * 100m), true, false, Now.AddMinutes(17)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, paymob.RefundCalls);
    }

    [Fact]
    public async Task Concurrent_different_success_transaction_is_returned_as_conflict()
    {
        string databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;
        var booking = Create(new DateOnly(2026, 10, 5));
        await using (var seed = new CareHomesDbContext(options))
        {
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        await using var db = new CareHomesDbContext(options);
        var competing = new ConcurrentSaveCareHomesDbContext(db, async () =>
        {
            await using var winner = new CareHomesDbContext(options);
            var current = await winner.Bookings.SingleAsync(x => x.Id == booking.Id);
            current.MarkPaid(8102, Now.AddMinutes(1));
            await winner.SaveChangesAsync();
        });

        var result = await new ConfirmPaymentHandler(competing, new FakePaymob()).Handle(
            new ConfirmCareHomePaymentCommand(booking.MerchantReference, 8101,
                decimal.ToInt64(booking.TotalAmount * 100m), true, false, Now.AddMinutes(2)), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.PaymentConflict", result.Error.Code);
    }

    [Fact]
    public async Task Owner_accept_losing_to_decision_expiry_returns_a_state_conflict()
    {
        string databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;
        Guid bookingId = Guid.Empty;
        UserId owner;
        await using (var seed = new CareHomesDbContext(options))
        {
            var facility = AddApprovedFacility(seed);
            owner = facility.OwnerUserId;
            var booking = CareHomeBooking.Create(facility.Id, UserId.New(), FamilyId.New(), ElderlyId.New(),
                Guid.NewGuid(), new DateOnly(2026, 10, 5), 100m, 0m, 0m, 1,
                "ع", "Elderly", 75, null, "Contact", null, null, null, Now);
            booking.MarkPaid(8200, Now.AddMinutes(1));
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using var db = new CareHomesDbContext(options);
        var competing = new ConcurrentSaveCareHomesDbContext(db, async () =>
        {
            await using var winner = new CareHomesDbContext(options);
            var current = await winner.Bookings.SingleAsync(x => x.Id == bookingId);
            current.ExpireIfNeeded(Now.AddHours(25));
            await winner.SaveChangesAsync();
        });
        var result = await new DecideHandler(competing, new FakePaymob()).Handle(
            new DecideCareHomeBookingCommand(owner, bookingId, true, null, Now.AddHours(2)), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidState", result.Error.Code);
        await using var verify = new CareHomesDbContext(options);
        Assert.Equal(CareHomeBookingStatus.RefundPending,
            (await verify.Bookings.SingleAsync(x => x.Id == bookingId)).Status);
    }

    [Fact]
    public async Task Extension_starts_at_paid_through_month_end_and_uses_remaining_same_type_capacity()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Shared", "Shared", 12000m, CareHomeAllocationMode.Shared, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "201", Now);
        CareHomeBed[] beds = [CareHomeBed.Create(facility.Id, room.Id, "A", Now), CareHomeBed.Create(facility.Id, room.Id, "B", Now)];
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        db.Beds.AddRange(beds);
        FamilyId family = FamilyId.New();
        var original = CareHomeBooking.Create(facility.Id, UserId.New(), family, ElderlyId.New(), type.Id,
            new DateOnly(2026, 10, 31), 12000m, 0m, 0m, 1, "ع", "Elderly", 75, null,
            "Contact", null, null, null, Now);
        original.MarkPaid(8300, Now.AddMinutes(1));
        original.Accept(Now.AddMinutes(2));
        db.Bookings.Add(original);
        await db.SaveChangesAsync();
        var provider = new FixedOccupancyProvider([
            new CareHomeOccupancyInterval(beds[0].Id, CareHomeResourceKind.Bed, original.EndDate, original.EndDate.AddMonths(1))]);

        var handler = new CreateCareHomeBookingExtensionHandler(db, new EffectiveCharges(), provider, new InlineReservationGuard());
        var crossFamily = await handler.Handle(new CreateCareHomeBookingExtensionCommand(UserId.New(), FamilyId.New(), original.Id, Now), default);
        Assert.False(crossFamily.IsSuccess);
        Assert.Equal("CareHomes.Bookings.NotFound", crossFamily.Error.Code);
        var result = await handler.Handle(new CreateCareHomeBookingExtensionCommand(UserId.New(), family, original.Id, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 11, 30), result.Value.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 30), result.Value.EndDate);
        Assert.Equal(original.Id, result.Value.ExtensionOfBookingId);
        Assert.Equal(CareHomeBookingStatus.Accepted, original.Status);
        Assert.Equal(12000m, result.Value.BaseAmount);
        Assert.Equal(120m, result.Value.PlatformFeeAmount);
        Assert.Equal(240m, result.Value.TaxAmount);
        var listed = await new FamilyListHandler(db).Handle(new ListFamilyCareHomeBookingsQuery(UserId.New(), family), default);
        Assert.Contains(listed.Value, x => x.Id == result.Value.Id && x.ExtensionOfBookingId == original.Id);

        var duplicate = await handler.Handle(new CreateCareHomeBookingExtensionCommand(UserId.New(), family, original.Id, Now), default);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidState", duplicate.Error.Code);
    }

    [Fact]
    public async Task Rejected_extension_is_fully_refunded_without_changing_the_current_stay()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Private", "Private", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "301", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        FamilyId family = FamilyId.New();
        var original = CareHomeBooking.Create(facility.Id, UserId.New(), family, ElderlyId.New(), type.Id,
            new DateOnly(2026, 10, 5), 12000m, 0m, 0m, 1, "ع", "Elderly", 75, null,
            "Contact", null, null, null, Now);
        original.MarkPaid(8301, Now.AddMinutes(1));
        original.Accept(Now.AddMinutes(2));
        db.Bookings.Add(original);
        await db.SaveChangesAsync();
        var paymob = new FakePaymob();
        var created = await new CreateCareHomeBookingExtensionHandler(db, new EffectiveCharges(),
            new EmptyCareHomeOccupancyProvider(), new InlineReservationGuard())
            .Handle(new CreateCareHomeBookingExtensionCommand(UserId.New(), family, original.Id, Now), default);
        Assert.True(created.IsSuccess);
        var extension = await db.Bookings.SingleAsync(x => x.Id == created.Value.Id);
        await new ConfirmPaymentHandler(db, paymob).Handle(new ConfirmCareHomePaymentCommand(
            extension.MerchantReference, 8302, decimal.ToInt64(extension.TotalAmount * 100m), true, false, Now.AddMinutes(1)), default);

        var rejected = await new DecideHandler(db, paymob).Handle(
            new DecideCareHomeBookingCommand(facility.OwnerUserId, extension.Id, false, "No capacity", Now.AddMinutes(2)), default);

        Assert.True(rejected.IsSuccess);
        Assert.Equal(CareHomeBookingStatus.Accepted, original.Status);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, extension.Status);
        Assert.Equal(extension.TotalAmount, extension.RefundAmount);
        Assert.Equal(1, paymob.RefundCalls);
    }

    [Fact]
    public async Task Extension_is_rejected_when_the_original_room_type_has_no_capacity_for_the_added_month()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Suite", "Suite", 15000m, CareHomeAllocationMode.Suite, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "501", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        FamilyId family = FamilyId.New();
        var original = CareHomeBooking.Create(facility.Id, UserId.New(), family, ElderlyId.New(), type.Id,
            new DateOnly(2026, 10, 5), 15000m, 0m, 0m, 1, "ع", "Elderly", 75, null,
            "Contact", null, null, null, Now);
        original.MarkPaid(8305, Now.AddMinutes(1));
        original.Accept(Now.AddMinutes(2));
        db.Bookings.Add(original);
        await db.SaveChangesAsync();
        var provider = new FixedOccupancyProvider([
            new CareHomeOccupancyInterval(room.Id, CareHomeResourceKind.Room, original.EndDate, original.EndDate.AddMonths(1))]);

        var result = await new CreateCareHomeBookingExtensionHandler(db, new EffectiveCharges(), provider, new InlineReservationGuard())
            .Handle(new CreateCareHomeBookingExtensionCommand(UserId.New(), family, original.Id, Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.CapacityConflict", result.Error.Code);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task Family_cancellation_refunds_future_extension_and_the_current_paid_stay()
    {
        await using var db = CreateBookingDb();
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Private", "Private", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "401", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        FamilyId family = FamilyId.New();
        var original = CareHomeBooking.Create(facility.Id, UserId.New(), family, ElderlyId.New(), type.Id,
            new DateOnly(2026, 10, 5), 12000m, 0m, 0m, 1, "ع", "Elderly", 75, null,
            "Contact", null, null, null, Now);
        original.MarkPaid(8303, Now.AddMinutes(1));
        original.Accept(Now.AddMinutes(2));
        var extension = CareHomeBooking.CreateExtension(original, UserId.New(), 12000m, 120m, 240m, 1, Now.AddMinutes(3));
        extension.MarkPaid(8304, Now.AddMinutes(4));
        extension.Accept(Now.AddMinutes(5));
        db.Bookings.AddRange(original, extension);
        await db.SaveChangesAsync();
        var paymob = new FakePaymob();

        var cancelled = await new CancelFamilyCareHomeBookingHandler(db, paymob).Handle(
            new CancelFamilyCareHomeBookingCommand(UserId.New(), family, original.Id, Now.AddHours(1)), default);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, original.Status);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, extension.Status);
        Assert.Equal(original.TotalAmount, original.RefundAmount);
        Assert.Equal(extension.TotalAmount, extension.RefundAmount);
        Assert.Equal(2, paymob.RefundCalls);
    }

    [Fact]
    public void Create_rejects_arrival_before_the_next_Cairo_calendar_date()
    {
        Assert.Throws<ArgumentException>(() => Create(new DateOnly(2026, 10, 3)));
        Assert.NotNull(Create(new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public void Payment_success_is_idempotent_but_conflicting_transaction_is_rejected()
    {
        var booking = Create(new DateOnly(2026, 10, 5));
        booking.MarkPaid(7001, Now.AddMinutes(1));
        booking.MarkPaid(7001, Now.AddMinutes(2));

        Assert.Equal(CareHomeBookingStatus.PaidAwaitingDecision, booking.Status);
        Assert.Equal(Now.AddHours(24).AddMinutes(1), booking.DecisionHoldUntilUtc);
        Assert.Throws<InvalidOperationException>(() => booking.MarkPaid(7002, Now.AddMinutes(3)));
    }

    [Fact]
    public void Late_success_after_checkout_expiry_enters_refund_path()
    {
        var booking = Create(new DateOnly(2026, 10, 5));
        booking.MarkPaid(7010, Now.AddMinutes(16));

        Assert.Equal(CareHomeBookingStatus.RefundPending, booking.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, booking.PaymentStatus);
        Assert.Equal("Payment succeeded after the checkout hold expired.", booking.DecisionReason);
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(17)));
    }

    [Fact]
    public void Payment_intent_claim_is_one_time_and_provider_failure_can_release_it()
    {
        var booking = Create(new DateOnly(2026, 10, 5));
        Assert.True(booking.TryClaimPaymentIntent(Now));
        Assert.False(booking.TryClaimPaymentIntent(Now.AddSeconds(1)));
        booking.ReleasePaymentIntentClaim(Now.AddSeconds(2));
        Assert.True(booking.TryClaimPaymentIntent(Now.AddSeconds(3)));
        booking.SetPaymentIntent(1, "order", "intent", "secret", "public", Now.AddSeconds(4));
        Assert.False(booking.TryClaimPaymentIntent(Now.AddSeconds(5)));
    }

    [Fact]
    public async Task Payment_intent_allows_family_member_scope_and_denies_cross_family_lookup()
    {
        await using var db = CreateBookingDb();
        UserId actor = UserId.New();
        FamilyId family = FamilyId.New();
        var booking = Create(new DateOnly(2026, 10, 5));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var client = new FakePaymob();
        var handler = new PaymentIntentHandler(db, client);

        var denied = await handler.Handle(new CreateCareHomePaymentIntentCommand(actor, FamilyId.New(), booking.Id, PaymentMethod.Card, Billing(), Now), default);
        Assert.False(denied.IsSuccess);
        Assert.Equal("CareHomes.Bookings.NotFound", denied.Error.Code);

        var allowedBooking = CareHomeBooking.Create(booking.FacilityId, actor, family, booking.ElderlyId, booking.RoomTypeId, booking.StartDate, 100m, 0m, 0m, 1, "Ø¹", "Elderly", 75, null, "Contact", null, null, null, Now);
        db.Bookings.Add(allowedBooking);
        await db.SaveChangesAsync();
        var allowed = await handler.Handle(new CreateCareHomePaymentIntentCommand(actor, family, allowedBooking.Id, PaymentMethod.Card, Billing(), Now), default);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(allowedBooking.Id, allowed.Value.BookingId);
    }

    [Fact]
    public async Task Payment_intent_provider_failure_releases_claim_for_retry()
    {
        await using var db = CreateBookingDb();
        var booking = Create(new DateOnly(2026, 10, 5));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var client = new FakePaymob { FailIntent = true };
        var handler = new PaymentIntentHandler(db, client);
        var command = new CreateCareHomePaymentIntentCommand(booking.FamilyUserId, booking.FamilyId, booking.Id, PaymentMethod.Wallet, Billing(), Now);

        var first = await handler.Handle(command, default);
        Assert.False(first.IsSuccess);
        client.FailIntent = false;
        var retry = await handler.Handle(command with { UtcNow = Now.AddSeconds(1) }, default);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, client.IntentCalls);
    }

    [Fact]
    public void Failed_payment_cancels_and_expired_checkout_becomes_expired()
    {
        var failed = Create(new DateOnly(2026, 10, 5));
        failed.MarkPaymentFailed(Now.AddMinutes(1));
        Assert.Equal(CareHomeBookingStatus.Cancelled, failed.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Failed, failed.PaymentStatus);

        var expired = Create(new DateOnly(2026, 10, 5));
        expired.ExpireIfNeeded(Now.AddMinutes(15));
        Assert.Equal(CareHomeBookingStatus.Expired, expired.Status);
    }

    [Fact]
    public void Accept_reject_and_decision_timeout_follow_the_paid_window()
    {
        var accepted = Create(new DateOnly(2026, 10, 5));
        accepted.MarkPaid(7001, Now);
        accepted.Accept(Now.AddHours(1));
        Assert.Equal(CareHomeBookingStatus.Accepted, accepted.Status);

        var rejected = Create(new DateOnly(2026, 10, 5));
        rejected.MarkPaid(7002, Now);
        rejected.Reject("  No suitable nurse  ", Now.AddHours(1));
        Assert.Equal(CareHomeBookingStatus.RefundPending, rejected.Status);
        Assert.Equal("No suitable nurse", rejected.DecisionReason);
        Assert.True(rejected.TryClaimRefund(Now.AddHours(1)));
        Assert.False(rejected.TryClaimRefund(Now.AddHours(1).AddSeconds(1)));
        rejected.MarkRefundInitiated("rf_7002", Now.AddHours(1).AddMinutes(1));
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, rejected.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.RefundInitiated, rejected.PaymentStatus);

        var timedOut = Create(new DateOnly(2026, 10, 5));
        timedOut.MarkPaid(7003, Now);
        timedOut.ExpireIfNeeded(Now.AddHours(24));
        Assert.Equal(CareHomeBookingStatus.RefundPending, timedOut.Status);
        Assert.Equal("Facility decision window expired.", timedOut.DecisionReason);
    }

    [Fact]
    public void HC035_family_refund_uses_recorded_check_in_not_facility_approval()
    {
        var awaitingDecision = Create(new DateOnly(2026, 10, 5));
        awaitingDecision.MarkPaid(7101, Now);
        Assert.Equal(awaitingDecision.TotalAmount, awaitingDecision.CancelByFamily(Now.AddMinutes(1)));

        var approved = Create(new DateOnly(2026, 10, 5));
        approved.MarkPaid(7102, Now);
        approved.Accept(Now.AddMinutes(1));
        Assert.Equal(approved.TotalAmount, approved.CancelByFamily(Now.AddMinutes(2)));

        var checkedIn = Create(new DateOnly(2026, 10, 5));
        checkedIn.MarkPaid(7103, Now);
        checkedIn.Accept(Now.AddMinutes(1));
        checkedIn.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(2));
        checkedIn.RecordCheckIn(UserId.New(), Now.AddMinutes(3));
        Assert.Equal(checkedIn.TotalAmount / 2m, checkedIn.CancelByFamily(Now.AddMinutes(4)));
        checkedIn.RecordCheckOut(UserId.New(), Now.AddMinutes(5));
        Assert.Throws<InvalidOperationException>(() => checkedIn.CancelByFamily(Now.AddMinutes(6)));
    }

    [Fact]
    public void HC035_unpaid_cancellation_releases_booking_without_refund_and_facility_cancellation_is_full()
    {
        var unpaid = Create(new DateOnly(2026, 10, 5));
        Assert.Equal(0m, unpaid.CancelByFamily(Now.AddMinutes(1)));
        Assert.Equal(CareHomeBookingStatus.Cancelled, unpaid.Status);
        Assert.Equal(CareHomeRefundStatus.None, unpaid.RefundStatus);

        var facilityCancelled = Create(new DateOnly(2026, 10, 5));
        facilityCancelled.MarkPaid(7104, Now);
        facilityCancelled.Accept(Now.AddMinutes(1));
        facilityCancelled.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(2));
        facilityCancelled.RecordCheckIn(UserId.New(), Now.AddMinutes(3));
        Assert.Equal(facilityCancelled.TotalAmount, facilityCancelled.CancelByFacility("Facility reason", Now.AddMinutes(4)));
        Assert.Equal(CareHomeRefundStatus.Pending, facilityCancelled.RefundStatus);
    }

    [Fact]
    public void HC035_refund_failure_can_be_retried_and_completion_is_idempotent()
    {
        var booking = Create(new DateOnly(2026, 10, 5));
        booking.MarkPaid(7105, Now);
        booking.Reject("No capacity", Now.AddMinutes(1));
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(2)));
        booking.MarkRefundFailed("Paymob.RefundRejected", Now.AddMinutes(3));
        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);

        Assert.True(booking.TryClaimRefund(Now.AddMinutes(4)));
        booking.MarkRefundInitiated("refund-7105", Now.AddMinutes(5));
        Assert.False(booking.MarkRefundCompletedFromProviderCallback("wrong", 7106, Now.AddMinutes(6)));
        Assert.True(booking.MarkRefundCompletedFromProviderCallback("refund-7105", 7105, Now.AddMinutes(6)));
        Assert.False(booking.MarkRefundCompletedFromProviderCallback("refund-7105", 7105, Now.AddMinutes(7)));
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
        Assert.Equal(CareHomeBookingPaymentStatus.Refunded, booking.PaymentStatus);
    }

    [Fact]
    public async Task HC035_failed_family_refund_is_retryable_once_through_admin_flow()
    {
        await using var db = CreateBookingDb();
        var booking = Create(new DateOnly(2026, 10, 5));
        booking.MarkPaid(7110, Now);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var paymob = new FakePaymob { FailRefund = true };
        var cancel = await new CancelFamilyCareHomeBookingHandler(db, paymob)
            .Handle(new CancelFamilyCareHomeBookingCommand(booking.FamilyUserId, booking.FamilyId, booking.Id, Now.AddMinutes(1)), default);

        Assert.False(cancel.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);
        Assert.Equal(booking.TotalAmount, booking.RefundAmount);
        paymob.FailRefund = false;

        var retry = await new RetryCareHomeRefundHandler(db, paymob)
            .Handle(new RetryCareHomeRefundCommand(UserId.New(), booking.Id, Now.AddMinutes(2)), default);
        Assert.True(retry.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Initiated, retry.Value.RefundStatus);
        Assert.Equal(2, paymob.RefundCalls);
        Assert.Equal(booking.TotalAmount, paymob.LastRefundAmount);

        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(9001, 7110, true, Now.AddMinutes(3)), default);
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
    }

    [Fact]
    public void Reject_requires_a_reason_and_paid_booking_cannot_be_accepted_after_timeout()
    {
        var rejected = Create(new DateOnly(2026, 10, 5));
        rejected.MarkPaid(7004, Now);
        Assert.Throws<ArgumentException>(() => rejected.Reject(" ", Now.AddHours(1)));

        var timedOut = Create(new DateOnly(2026, 10, 5));
        timedOut.MarkPaid(7005, Now);
        Assert.Throws<InvalidOperationException>(() => timedOut.Accept(Now.AddHours(24)));
    }

    [Fact]
    public async Task Checkout_fails_closed_when_no_effective_finance_rule_exists()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Room", "Room", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var result = await new CheckoutHandler(db, new NoCharges(), new EmptyCareHomeOccupancyProvider(), new InlineReservationGuard())
            .Handle(new CheckoutCareHomeBookingCommand(
                UserId.New(), FamilyId.New(), new ElderlyIntakeResolution(
                    FamilyId.New(), ElderlyId.New(), UserId.New(), "Ø¹Ø±Ø¨ÙŠ", "Elderly", 75,
                    new ElderlyIntakeMedicalProjection("Unknown", null, null, [], [], [], null),
                    new ElderlyIntakeResponsibleContact("Contact", "+201000000000", null), null),
                facility.Id.Value, type.Id, new DateOnly(2026, 10, 5), Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.ChargesNotConfigured", result.Error.Code);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Checkout_maps_reservation_capacity_conflict_to_stable_error()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        CareHomeFacility facility = AddApprovedFacility(db);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Room", "Room", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var result = await new CheckoutHandler(db, new EffectiveCharges(), new EmptyCareHomeOccupancyProvider(), new ConflictReservationGuard())
            .Handle(new CheckoutCareHomeBookingCommand(
                UserId.New(), FamilyId.New(), Intake(), facility.Id.Value, type.Id, new DateOnly(2026, 10, 5), Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.CapacityConflict", result.Error.Code);
    }

    private static CareHomeBooking Create(DateOnly start, decimal baseAmount = 10000m, decimal fee = 500m, decimal tax = 525m) =>
        CareHomeBooking.Create(
            CareHomeId.New(), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(), start,
            baseAmount, fee, tax, 4, "Ù…ØµØ±ÙŠ", "Elderly", 75, "{\"bloodType\":\"Unknown\"}",
            "Family member", "+201000000000", "Daughter", "Needs assistance", Now);

    private static CareHomesDbContext CreateBookingDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CheckoutHandler CreateCheckoutHandler(CareHomesDbContext db) =>
        new(db, new EffectiveCharges(), new EmptyCareHomeOccupancyProvider(), new InlineReservationGuard());

    private static PaymobBillingData Billing() => new("Fixture", "Family", "family@test.local", "+201000000000");

    private static ElderlyIntakeResolution Intake() => new(
        FamilyId.New(), ElderlyId.New(), UserId.New(), "Ø¹Ø±Ø¨ÙŠ", "Elderly", 75,
        new ElderlyIntakeMedicalProjection("Unknown", null, null, [], [], [], null),
        new ElderlyIntakeResponsibleContact("Contact", "+201000000000", null), null);

    private static async Task<CareHomeRoomType> AddBookableRoomType(CareHomesDbContext db, CareHomeFacility facility)
    {
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Room", "Room", 12000m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return type;
    }

    private static CareHomeFacility AddApprovedFacility(CareHomesDbContext db, DateOnly? licenseExpiry = null)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, documentType, $"private/{documentType}.pdf", "application/pdf", 10,
                documentType == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
        {
            DateOnly? expiryDate = document.Type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null;
            facility.VerifyDocument(admin, facility.Version, document.Id, expiryDate, expiryDate is null, Now);
        }
        DateOnly approvalDate = licenseExpiry is { } expiry && expiry < new DateOnly(2026, 10, 3)
            ? expiry.AddDays(-1)
            : new DateOnly(2026, 10, 3);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, approvalDate);
        db.Facilities.Add(facility);
        return facility;
    }

    private sealed class NoCharges : IPlatformChargeRuleReader
    {
        public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult<PlatformChargeRuleRates?>(null);
        public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    }

    private sealed class InlineReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class ConflictReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken) =>
            throw new CareHomeCapacityConflictException();
    }

    private sealed class EffectiveCharges : IPlatformChargeRuleReader
    {
        public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult<PlatformChargeRuleRates?>(new(1m, 2m, 1));
        public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    }

    private sealed class FixedOccupancyProvider(IReadOnlyList<CareHomeOccupancyInterval> intervals) : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken) => Task.FromResult(intervals);
    }

    private sealed class FakePaymob : IPaymobClient
    {
        public bool FailIntent { get; set; }
        public bool FailRefund { get; set; }
        public int IntentCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public decimal LastRefundAmount { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(PaymobPaymentIntentInput input, CancellationToken cancellationToken = default)
        {
            IntentCalls++;
            return Task.FromResult(FailIntent
                ? Result<PaymobPaymentIntent>.Failure(new Error("Paymob.GatewayError", "temporary"))
                : Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent("order", "intent", "secret", "public")));
        }

        public Task<Result<string?>> RefundPaymentAsync(string paymobTransactionId, decimal amount, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            LastRefundAmount = amount;
            return Task.FromResult(FailRefund
                ? Result<string?>.Failure(new Error("Paymob.RefundRejected", "temporary"))
                : Result<string?>.Success("refund"));
        }
    }

    private sealed class ConcurrentSaveCareHomesDbContext(
        CareHomesDbContext inner,
        Func<Task> competingCommit) : ICareHomesDbContext
    {
        private bool _raceTriggered;

        public Microsoft.EntityFrameworkCore.DbSet<CareHomeFacility> Facilities => inner.Facilities;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeRoomType> RoomTypes => inner.RoomTypes;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeRoom> Rooms => inner.Rooms;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeBed> Beds => inner.Beds;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => inner.MaintenanceBlocks;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeBooking> Bookings => inner.Bookings;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeRating> Ratings => inner.Ratings;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomePayout> Payouts => inner.Payouts;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomePayoutDebt> PayoutDebts => inner.PayoutDebts;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => inner.BookingAssignmentHistory;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => inner.TransferNotificationOutbox;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;
        public Microsoft.EntityFrameworkCore.DbSet<CareHomeProfileMedia> ProfileMedia => inner.ProfileMedia;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_raceTriggered)
            {
                _raceTriggered = true;
                await competingCommit();
                throw new DbUpdateConcurrencyException("Simulated competing callback commit.");
            }
            return await inner.SaveChangesAsync(cancellationToken);
        }
    }
}
