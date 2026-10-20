using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// F6 caregiver booking detail. The read must publish the persisted cancellation
/// facts (actor side, action, reason category, refund entitlement, refund
/// decision reason and a fact-aware refund state) and nothing invented: no status
/// ledger, no event history and no derived timeline, and never the recorded
/// caregiver-incident flag or the free-text reason note. The caregiver-only
/// boundary must stay exactly as it was: a booking that belongs to another
/// caregiver is not found.
/// </summary>
public sealed class CaregiverBookingDetailTests
{
    [Fact]
    public async Task Detail_publishes_the_persisted_cancellation_facts_and_a_fact_aware_refund_state()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId actorUserId = UserId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelledOnUtc = now.AddMinutes(2);
        booking.CancelByCaregiver("SECRET_BOOKING_REASON", cancelledOnUtc);
        BookingCancellationFact fact = CreateCaregiverCancelFact(
            booking.Id,
            actorUserId,
            booking.ConfirmedOnUtc!.Value,
            cancelledOnUtc,
            "SECRET_REASON_NOTE");

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        BookingDetailResponse response = result.Value;

        // The added values are read from the stored fact; none is derived.
        Assert.Equal(BookingCancellationActorSide.Caregiver, response.CancellationActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, response.CancellationAction);
        Assert.Equal(BookingCancellationReasonCategory.Emergency.ToString(), response.CancellationReasonCategory);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, response.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, response.RefundDecisionReason);

        // Paid, cancelled after acceptance and never refunded: the fact-aware
        // state is the existing failed-refund outcome, not a new one.
        Assert.Equal(BookingRefundState.Failed, response.RefundState);

        // The pre-existing fields keep their meaning: the booking's own reason
        // and timestamps are unchanged, and the persisted lifecycle instants are
        // reported rather than replaced by the fact's instants.
        Assert.Equal(booking.Status, response.Status);
        Assert.Equal(booking.CancellationReason, response.CancellationReason);
        Assert.Equal(booking.CancelledOnUtc, response.CancelledOnUtc);
        Assert.Equal(booking.PaidOnUtc, response.PaidOnUtc);
        Assert.Equal(booking.ConfirmedOnUtc, response.ConfirmedOnUtc);
    }

    [Fact]
    public async Task Detail_reports_no_cancellation_facts_when_none_was_recorded()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CancellationActorSide);
        Assert.Null(result.Value.CancellationAction);
        Assert.Null(result.Value.CancellationReasonCategory);
        Assert.Null(result.Value.RefundEntitlement);
        Assert.Null(result.Value.RefundDecisionReason);
        Assert.Equal(BookingRefundState.NotApplicable, result.Value.RefundState);
    }

    [Fact]
    public async Task Detail_keeps_the_caregiver_only_boundary_and_never_serves_another_caregivers_booking()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var owner = CaregiverId.New();
        var stranger = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, owner, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var other = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(stranger, booking.Id), CancellationToken.None);
        var unknown = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(owner, BookingId.New()), CancellationToken.None);

        foreach (var denied in new[] { other, unknown })
        {
            Assert.False(denied.IsSuccess);
            Assert.Equal("Bookings.NotFound", denied.Error.Code);
        }
    }

    [Fact]
    public async Task Detail_never_exposes_the_caregiver_incident_flag_a_ledger_or_a_derived_timeline()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId actorUserId = UserId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelledOnUtc = now.AddMinutes(2);
        booking.CancelByCaregiver("SECRET_BOOKING_REASON", cancelledOnUtc);
        BookingCancellationFact fact = CreateCaregiverCancelFact(
            booking.Id,
            actorUserId,
            booking.ConfirmedOnUtc!.Value,
            cancelledOnUtc,
            "SECRET_REASON_NOTE");

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        string json = JsonSerializer.Serialize(result.Value);

        Assert.DoesNotContain("SECRET_REASON_NOTE", json, StringComparison.Ordinal);
        Assert.DoesNotContain(actorUserId.Value.ToString(), json, StringComparison.OrdinalIgnoreCase);

        foreach (string forbidden in new[]
                 {
                     "isCaregiverIncident", "reasonNote", "actorUserId", "policyVersion",
                     "statusAtCancellation", "ledger", "timeline", "eventHistory", "history"
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = CreateDbContext();
        var family = Family.Create(UserId.New(), "Caregiver Detail Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (dbContext, family, elderly);
    }

    private static Booking ConfirmedBooking(
        Family family,
        Elderly elderly,
        CaregiverId caregiverId,
        DateTime now)
    {
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        booking.AcceptByCaregiver(now.AddMinutes(1));

        return booking;
    }

    private static BookingCancellationFact CreateCaregiverCancelFact(
        BookingId bookingId,
        UserId actorUserId,
        DateTime confirmedOnUtc,
        DateTime cancelledOnUtc,
        string note)
    {
        var feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            note);

        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                startedOnUtc: null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));

        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }
}
