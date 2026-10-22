using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Aggregate-level revision-2 refund behavior on <see cref="Booking"/>: the preserved policy
/// snapshot, session allocation and delivery recording, the refund money ledger and its ceiling,
/// and the cancellation-fact description seam that keeps the recorded fact in agreement with the
/// awarded amount.
/// <para>
/// NOT RUN. Authored in the W3-F1 slice; the single build/test gate belongs to Mastermind.
/// </para>
/// </summary>
public sealed class BookingRefundAggregateTests
{
    private static readonly DateTime ScheduledStartUtc =
        new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CreatedOnUtc =
        new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------------
    // Revision preservation at booking time.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NewBookingWithoutAnExplicitRevision_DefaultsToTheLegacyRevision()
    {
        Booking booking = NewBooking(refundPolicyVersion: null);

        Assert.Equal(BookingRefundPolicyVersion.LegacyVersion, booking.RefundPolicyVersion);
        Assert.Equal(default, booking.RefundPolicyDocumentEffectiveOn);
    }

    [Fact]
    public void NewBookingWithTheActivatedRevision_RecordsRevisionAndDocumentDate()
    {
        Booking booking = NewBooking(
            BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: true));

        Assert.Equal(2, booking.RefundPolicyVersion);
        Assert.Equal(
            BookingRefundPolicyVersion.PolicyDocumentEffectiveOn,
            booking.RefundPolicyDocumentEffectiveOn);
    }

    // ---------------------------------------------------------------------------------------------
    // Session allocations (decision #5).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AttachSessionAllocations_DistributesThePaidTotalExactlyOnce()
    {
        Booking booking = NewBooking();

        booking.AttachSessionAllocations(paidPackageMinorUnits: 100000, sessionCount: 3);

        Assert.Equal(3, booking.SessionAllocations.Count);
        Assert.Equal(100000, booking.SessionAllocations.Sum(a => a.AllocatedMinorUnits));
    }

    [Fact]
    public void AttachSessionAllocations_IsRefusedASecondTime()
    {
        Booking booking = NewBooking();
        booking.AttachSessionAllocations(100000, 3);

        Assert.Throws<DomainException>(() => booking.AttachSessionAllocations(100000, 3));
    }

    [Fact]
    public void RecordSessionDelivery_PreservesTheAllocatedAmountAndMarksItDelivered()
    {
        Booking booking = NewBooking();
        booking.AttachSessionAllocations(100000, 3);
        long before = booking.SessionAllocations[0].AllocatedMinorUnits;

        BookingSessionAllocation delivered =
            booking.RecordSessionDelivery(1, ScheduledStartUtc.AddHours(-30), CreatedOnUtc);

        Assert.True(delivered.IsDelivered);
        Assert.Equal(before, delivered.AllocatedMinorUnits);
        Assert.Equal(ScheduledStartUtc.AddHours(-30), delivered.DeliveredOnUtc);
    }

    [Fact]
    public void RecordSessionDelivery_OnAnUnknownSession_IsRefused()
    {
        Booking booking = NewBooking();
        booking.AttachSessionAllocations(100000, 3);

        Assert.Throws<DomainException>(
            () => booking.RecordSessionDelivery(9, ScheduledStartUtc, CreatedOnUtc));
    }

    [Fact]
    public void RecordSessionProviderFailure_MakesTheSessionRefundableInFull()
    {
        Booking booking = NewBooking();
        booking.AttachSessionAllocations(100000, 3);

        BookingSessionAllocation failed = booking.RecordSessionProviderFailure(2, CreatedOnUtc);

        Assert.Equal(BookingSessionOutcome.ProviderFailed, failed.Outcome);
        Assert.False(failed.IsDelivered);
        Assert.Equal(33333L, failed.AllocatedMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Refund money ledger and ceiling (decision #4).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RecordRefundedAmount_AccumulatesAndBoundsTheCeiling()
    {
        Booking booking = PaidBooking();

        booking.RecordRefundedAmount(15000, CreatedOnUtc);

        Assert.Equal(15000, booking.RefundedMinorUnits);
        Assert.Equal(25000, booking.RefundableCeilingMinorUnits());
    }

    [Fact]
    public void RecordRefundedAmount_RejectsAZeroOrNegativeAmount()
    {
        Booking booking = PaidBooking();

        Assert.Throws<DomainException>(() => booking.RecordRefundedAmount(0, CreatedOnUtc));
        Assert.Throws<DomainException>(() => booking.RecordRefundedAmount(-1, CreatedOnUtc));
    }

    [Fact]
    public void RefundableCeiling_IsZeroForAnUnpaidBooking()
    {
        Booking booking = NewBooking();

        Assert.Equal(0, booking.RefundableCeilingMinorUnits());
    }

    // ---------------------------------------------------------------------------------------------
    // The cancellation-fact description seam.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DescribeForCancellationFact_InsideTheWindow_PersistsThePartialAwardFaithfully()
    {
        // MAJ-6: DescribeForCancellationFact() saturates a non-zero award to FullCapturedRefund for
        // decision-version-1 readers, but the fact must NOT store that saturation — a 50% clause-2
        // award would then be indistinguishable from a full refund on the wire.
        Booking booking = PaidBooking();
        booking.AcceptByCaregiver(CreatedOnUtc.AddHours(1));

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc - TimeSpan.FromHours(2),
                feedback));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            UserId.New(),
            decision,
            feedback);

        Assert.Equal(BookingRefundEntitlement.PartialRefund, fact.RefundEntitlement);
        Assert.Equal(20000, fact.AwardedMinorUnits);
        Assert.Equal(40000, fact.PartialEntitlementMinorUnits);

        // The description still classifies the reason and the incident flag for legacy readers.
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, fact.RefundDecisionReason);
    }

    [Fact]
    public void DescribeForCancellationFact_OutsideTheWindow_PersistsAFullAwardWithNoPartialBasis()
    {
        Booking booking = PaidBooking();
        booking.AcceptByCaregiver(CreatedOnUtc.AddHours(1));

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc - TimeSpan.FromHours(25),
                feedback));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            UserId.New(),
            decision,
            feedback);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(40000, fact.AwardedMinorUnits);
        Assert.Null(fact.PartialEntitlementMinorUnits);
    }

    [Fact]
    public void DescribeForCancellationFact_NeverReportsNothingCapturedForAPaidBooking()
    {
        Booking booking = PaidBooking();
        booking.AcceptByCaregiver(CreatedOnUtc.AddHours(1));

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc,
                feedback));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            UserId.New(),
            decision,
            feedback);

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow, fact.RefundDecisionReason);
        Assert.NotEqual(BookingRefundDecisionReason.NothingCaptured, fact.RefundDecisionReason);
    }

    [Fact]
    public void DescribeForCancellationFact_APartialAwardPersistsItsPartialEntitlementAndBasis()
    {
        // MAJ-6: a 50% award is recorded as PartialRefund with the basis it was a slice of, so the
        // wire never labels it a full refund and ops retry can reconstruct the amount.
        Booking booking = PaidBooking();
        booking.AcceptByCaregiver(CreatedOnUtc.AddHours(1));

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc - TimeSpan.FromHours(2),
                feedback));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            UserId.New(),
            decision,
            feedback);

        Assert.Equal(BookingRefundEntitlement.PartialRefund, fact.RefundEntitlement);
        Assert.Equal(20000, fact.AwardedMinorUnits);
        Assert.Equal(40000, fact.PartialEntitlementMinorUnits);
    }

    [Fact]
    public void DescribeForCancellationFact_OnAnAcceptedBooking_StillRequiresTheFeedbackShape()
    {
        Booking booking = PaidBooking();
        booking.AcceptByCaregiver(CreatedOnUtc.AddHours(1));

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc - TimeSpan.FromHours(2),
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "family emergency")));

        Assert.Throws<DomainException>(() =>
            BookingCancellationFact.Create(booking.Id, UserId.New(), decision, feedback: null));
    }

    // ---------------------------------------------------------------------------------------------
    // Session-outcome saturation (minor 16).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RecordSessionProviderFailure_OnAnAlreadyFailedSession_IsRefused()
    {
        Booking booking = PaidPackageBooking(40000, 2);

        booking.RecordSessionProviderFailure(sessionIndex: 1, utcNow: CreatedOnUtc);

        // A second failure claim would invent evidence for a failure that already happened.
        Assert.Throws<DomainException>(() =>
            booking.RecordSessionProviderFailure(sessionIndex: 1, utcNow: CreatedOnUtc));
    }

    [Fact]
    public void RecordSessionProviderFailure_OnADeliveredSession_IsRefused()
    {
        Booking booking = PaidPackageBooking(40000, 2);
        booking.RecordSessionDelivery(
            sessionIndex: 1,
            deliveredOnUtc: CreatedOnUtc.AddHours(1),
            utcNow: CreatedOnUtc.AddHours(1));

        // Delivered care stays payable; it can never be re-labelled a provider failure.
        Assert.Throws<DomainException>(() =>
            booking.RecordSessionProviderFailure(sessionIndex: 1, utcNow: CreatedOnUtc.AddHours(2)));
    }

    [Fact]
    public void RecordSessionDelivery_OnAProviderFailedSession_IsRefused()
    {
        Booking booking = PaidPackageBooking(40000, 2);
        booking.RecordSessionProviderFailure(sessionIndex: 2, utcNow: CreatedOnUtc);

        // The stricter transition set: a failed session cannot be claimed as delivered either.
        Assert.Throws<DomainException>(() =>
            booking.RecordSessionDelivery(
                sessionIndex: 2,
                deliveredOnUtc: CreatedOnUtc.AddHours(1),
                utcNow: CreatedOnUtc.AddHours(1)));
    }

    [Fact]
    public void RecordSessionProviderFailure_OnAnUndeliveredSession_IsAllowed()
    {
        Booking booking = PaidPackageBooking(40000, 2);

        BookingSessionAllocation failed =
            booking.RecordSessionProviderFailure(sessionIndex: 2, utcNow: CreatedOnUtc);

        Assert.Equal(BookingSessionOutcome.ProviderFailed, failed.Outcome);
        Assert.Equal(20000, failed.AllocatedMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static Booking NewBooking(BookingRefundPolicyVersion? refundPolicyVersion = null) =>
        Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc).AddHours(2),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 0m, 0m, 1),
            acceptanceDeadlineUtc: ScheduledStartUtc.AddHours(-1),
            currentDate: DateOnly.FromDateTime(CreatedOnUtc),
            createdOnUtc: CreatedOnUtc,
            refundPolicyVersion: refundPolicyVersion);

    private static Booking PaidBooking()
    {
        Booking booking = NewBooking();

        // MAJ-7: the intent must exist before the payment, or ResolveCaptureEvidence reports
        // Ambiguous and the policy correctly refuses to decide.
        booking.RecordPaymentIntent("order", PaymentMethod.Card, CreatedOnUtc);
        booking.MarkAsPaid("order", "txn", CreatedOnUtc);
        return booking;
    }

    private static Booking PaidPackageBooking(long paidPackageMinorUnits, int sessionCount)
    {
        Booking booking = NewBooking();

        // Package sessions are allocated at purchase, before the booking is paid.
        booking.AttachSessionAllocations(paidPackageMinorUnits, sessionCount);
        booking.RecordPaymentIntent("order", PaymentMethod.Card, CreatedOnUtc);
        booking.MarkAsPaid("order", "txn", CreatedOnUtc);
        return booking;
    }
}
