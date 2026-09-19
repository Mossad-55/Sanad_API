using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Elderlies;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingRefundStateResolutionTests
{
    private static Booking CreatePaidCancelledBooking(DateTime now, Family family, Elderly elderly, CaregiverId caregiverId)
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
        booking.CancelByCaregiver("paid cancelled", now.AddMinutes(2));
        return booking;
    }

    private static Booking CreateUnpaidCancelledBooking(DateTime now, Family family, Elderly elderly, CaregiverId caregiverId)
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

        booking.CancelByFamily("unpaid cancelled", now.AddMinutes(1));
        return booking;
    }

    private static Booking CreateActiveBooking(DateTime now, Family family, Elderly elderly, CaregiverId caregiverId)
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

    private static Family CreateFamily()
    {
        return Family.Create(UserId.New(), "RefundState Family");
    }

    private static Elderly CreateElderly(Family family)
    {
        return Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static BookingCancellationFact CreateFactWithEntitlement(
        BookingId bookingId,
        UserId actorUserId,
        BookingRefundEntitlement entitlement,
        BookingRefundDecisionReason reason,
        BookingCancellationActorSide actorSide = BookingCancellationActorSide.Caregiver,
        BookingCancellationAction action = BookingCancellationAction.Cancel)
    {
        // Build a decision that yields the requested entitlement/reason.
        // We use the policy to create a valid decision, then create fact.
        // For NoRefundDue we use family outside grace; for FullCapturedRefund we use caregiver cancel.
        if (entitlement == BookingRefundEntitlement.NoRefundDue && reason == BookingRefundDecisionReason.NothingCaptured)
        {
            var feedback = BookingCancellationFeedback.CreateOptionalNote("unpaid note");
            var decision = BookingCancellationPolicy.Decide(
                BookingCancellationPolicyInput.ForFamilyCancellation(
                    BookingStatus.PendingPayment,
                    null,
                    null,
                    new DateTime(2026, 3, 10, 9, 10, 0, DateTimeKind.Utc),
                    BookingCaptureEvidence.NotCaptured,
                    feedback));
            return BookingCancellationFact.Create(bookingId, actorUserId, decision);
        }

        if (entitlement == BookingRefundEntitlement.NoRefundDue)
        {
            // Paid NoRefundDue: family cancellation outside grace window on a confirmed booking
            DateTime acceptance = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
            DateTime cancelled = acceptance.AddMinutes(70); // outside 60 window
            var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Emergency, "family emergency outside grace");
            var decision = BookingCancellationPolicy.Decide(
                BookingCancellationPolicyInput.ForFamilyCancellation(
                    BookingStatus.Confirmed,
                    acceptance,
                    null,
                    cancelled,
                    BookingCaptureEvidence.Captured,
                    feedback));
            return BookingCancellationFact.Create(bookingId, actorUserId, decision);
        }

        // FullCapturedRefund
        {
            DateTime acceptance = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
            DateTime cancelled = acceptance.AddMinutes(10);
            var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Emergency, "caregiver emergency");
            var decision = BookingCancellationPolicy.Decide(
                BookingCancellationPolicyInput.ForCaregiverCancellation(
                    BookingStatus.Confirmed,
                    acceptance,
                    null,
                    cancelled,
                    BookingCaptureEvidence.Captured,
                    feedback));
            return BookingCancellationFact.Create(bookingId, actorUserId, decision);
        }
    }

    [Fact]
    public void ResolveWithFact_StatusRefunded_NoRefundDueFact_ReturnsSucceeded()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreatePaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.NoRefundDue, BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow);

        booking.MarkRefunded("refund-txn-1", now.AddMinutes(5));

        var state = BookingRefundStates.ResolveWithFact(booking, fact);

        Assert.Equal(BookingRefundState.Succeeded, state);
    }

    [Fact]
    public void ResolveWithFact_RefundedOnUtcSet_NoRefundDueFact_ReturnsSucceeded()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreatePaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.NoRefundDue, BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow);

        booking.MarkRefunded("refund-txn-2", now.AddMinutes(5));

        // Both status Refunded and RefundedOnUtc are set; this pins the RefundedOnUtc branch
        Assert.NotNull(booking.RefundedOnUtc);
        Assert.Equal(BookingStatus.Refunded, booking.Status);

        var state = BookingRefundStates.ResolveWithFact(booking, fact);

        Assert.Equal(BookingRefundState.Succeeded, state);
    }

    [Fact]
    public void ResolveWithFact_UnpaidCancelled_NoRefundDue_NothingCaptured_ReturnsNoRefundDue()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreateUnpaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.NoRefundDue, BookingRefundDecisionReason.NothingCaptured);

        var state = BookingRefundStates.ResolveWithFact(booking, fact);

        Assert.Equal(BookingRefundState.NoRefundDue, state);
        // Legacy Resolve would be NotApplicable for unpaid; fact-aware is NoRefundDue
        Assert.Equal(BookingRefundState.NotApplicable, BookingRefundStates.Resolve(booking));
    }

    [Fact]
    public void ResolveWithFact_PaidCancelled_NoRefundDue_ReturnsNoRefundDue_NotFailed()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreatePaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.NoRefundDue, BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow);

        var state = BookingRefundStates.ResolveWithFact(booking, fact);
        var legacy = BookingRefundStates.Resolve(booking);

        Assert.Equal(BookingRefundState.NoRefundDue, state);
        Assert.Equal(BookingRefundState.Failed, legacy); // legacy renders as Failed — the defect
        Assert.NotEqual(BookingRefundState.Failed, state);
    }

    [Fact]
    public void ResolveWithFact_PaidCancelled_FullCapturedRefund_NotRefunded_ReturnsFailed()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreatePaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.FullCapturedRefund, BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance);

        var state = BookingRefundStates.ResolveWithFact(booking, fact);

        Assert.Equal(BookingRefundState.Failed, state);
    }

    [Fact]
    public void ResolveWithFact_PaidCancelled_FullCapturedRefund_Refunded_ReturnsSucceeded()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);
        var caregiverId = CaregiverId.New();
        var booking = CreatePaidCancelledBooking(now, family, elderly, caregiverId);
        var fact = CreateFactWithEntitlement(booking.Id, UserId.New(), BookingRefundEntitlement.FullCapturedRefund, BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance);

        booking.MarkRefunded("refund-txn-3", now.AddMinutes(10));

        var state = BookingRefundStates.ResolveWithFact(booking, fact);

        Assert.Equal(BookingRefundState.Succeeded, state);
    }

    [Fact]
    public void ResolveWithFact_NoFact_MatchesLegacyResolve()
    {
        DateTime now = DateTime.UtcNow;
        var family = CreateFamily();
        var elderly = CreateElderly(family);

        var paidCancelled = CreatePaidCancelledBooking(now, family, elderly, CaregiverId.New());
        Assert.Equal(BookingRefundStates.Resolve(paidCancelled), BookingRefundStates.ResolveWithFact(paidCancelled, null));
        Assert.Equal(BookingRefundState.Failed, BookingRefundStates.ResolveWithFact(paidCancelled, null));

        var unpaidCancelled = CreateUnpaidCancelledBooking(now, family, elderly, CaregiverId.New());
        Assert.Equal(BookingRefundStates.Resolve(unpaidCancelled), BookingRefundStates.ResolveWithFact(unpaidCancelled, null));
        Assert.Equal(BookingRefundState.NotApplicable, BookingRefundStates.ResolveWithFact(unpaidCancelled, null));

        var active = CreateActiveBooking(now, family, elderly, CaregiverId.New());
        Assert.Equal(BookingRefundStates.Resolve(active), BookingRefundStates.ResolveWithFact(active, null));
        Assert.Equal(BookingRefundState.NotApplicable, BookingRefundStates.ResolveWithFact(active, null));
    }
}
