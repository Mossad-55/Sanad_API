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

    private static PaymobBillingData Billing() => new("Fixture", "Family", "family@test.local", "+201000000000");

    private static ElderlyIntakeResolution Intake() => new(
        FamilyId.New(), ElderlyId.New(), UserId.New(), "Ø¹Ø±Ø¨ÙŠ", "Elderly", 75,
        new ElderlyIntakeMedicalProjection("Unknown", null, null, [], [], [], null),
        new ElderlyIntakeResponsibleContact("Contact", "+201000000000", null), null);

    private static CareHomeFacility AddApprovedFacility(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, documentType, $"private/{documentType}.pdf", "application/pdf", 10, null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, new DateOnly(2026, 10, 3));
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

    private sealed class FakePaymob : IPaymobClient
    {
        public bool FailIntent { get; set; }
        public int IntentCalls { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(PaymobPaymentIntentInput input, CancellationToken cancellationToken = default)
        {
            IntentCalls++;
            return Task.FromResult(FailIntent
                ? Result<PaymobPaymentIntent>.Failure(new Error("Paymob.GatewayError", "temporary"))
                : Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent("order", "intent", "secret", "public")));
        }

        public Task<Result<string?>> RefundPaymentAsync(string paymobTransactionId, decimal amount, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success("refund"));
    }
}
