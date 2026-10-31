using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Domain.Bookings;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Policy matrix for the approved revision-2 <see cref="BookingRefundPolicy"/>: the 24-hour / 50% /
/// no-refund-at-start rules measured against the scheduled service start (clause 2), the
/// multi-session discounted allocation rules (clause 3 and Owner decision #5), the provider
/// failure, access, safety and force-majeure outcomes (clause 4 and its Owner amendment), the
/// refundable ceiling (Owner decision #4), the zero-award floor (decision #31) and the
/// half-to-even proration that mirrors the Finance money core.
/// <para>
/// Every timestamp is a fixed UTC constant; the policy reads no clock and these tests never sleep.
/// The scheduled start is supplied explicitly, so the boundary under test is tick-exact.
/// </para>
/// <para>
/// NOT RUN. This file is authored as part of the W3-F1 refund implementation slice; the single
/// dotnet build/test gate is reserved to Mastermind, so nothing here has been executed.
/// </para>
/// </summary>
public sealed class BookingRefundPolicyTests
{
    private static readonly DateTime ScheduledStartUtc =
        new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Exactly 24 hours before start: the last instant a full refund is due.</summary>
    private static readonly DateTime Exactly24HoursBefore = ScheduledStartUtc - TimeSpan.FromHours(24);

    /// <summary>Tick-exactly inside the window (one tick past the 24-hour boundary).</summary>
    private static readonly DateTime OneTickInsideWindow = Exactly24HoursBefore.AddTicks(1);

    /// <summary>Two hours before start: inside the window, so a 50% award.</summary>
    private static readonly DateTime TwoHoursBefore = ScheduledStartUtc - TimeSpan.FromHours(2);

    private static readonly DateTime AcceptedAtUtc =
        new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------------
    // Published rule constants: the literals below stand in for these.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PublishedConstants_MatchTheApprovedRule()
    {
        Assert.Equal(2, BookingRefundPolicy.CurrentPolicyVersion);
        Assert.Equal(TimeSpan.FromHours(24), BookingRefundPolicy.FullRefundWindow);
        Assert.Equal(50, BookingRefundPolicy.WithinWindowRefundPercentage);
        Assert.Equal(60, BookingRefundPolicy.ProviderLatenessThresholdMinutes);
        Assert.Equal(TimeSpan.FromHours(48), BookingRefundPolicy.ComplaintReportingWindow);
    }

    // ---------------------------------------------------------------------------------------------
    // Clause 2: the family cancels against the scheduled start.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FamilyCancellation_AtExactly24Hours_RefundsTheWholeCapturedAmount()
    {
        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(Exactly24HoursBefore, captured: 40000));

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(40000, decision.AwardedMinorUnits);
        Assert.Equal(40000, decision.RefundableCeilingMinorUnits);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, decision.RefundDecisionReason);
        Assert.Null(decision.OutcomeReason);
        Assert.Equal(2, decision.PolicyVersion.Version);
    }

    [Fact]
    public void FamilyCancellation_OneTickInsideWindow_RefundsHalfRoundedHalfToEven()
    {
        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(OneTickInsideWindow, captured: 40000));

        Assert.Equal(20000, decision.AwardedMinorUnits);
        Assert.Equal(40000, decision.AwardedProrationBasisUnits);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, decision.RefundDecisionReason);
    }

    [Fact]
    public void FamilyCancellation_OddCapturedAmount_RoundsHalfToEvenAtTheProrationFloor()
    {
        // 20001 / 2 = 10000.5 → the tie rounds to the even candidate, 10000.
        BookingRefundDecision even = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(TwoHoursBefore, captured: 20001));

        Assert.Equal(10000, even.AwardedMinorUnits);

        // 20002 / 2 = 10001 exactly, no tie.
        BookingRefundDecision exact = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(TwoHoursBefore, captured: 20002));

        Assert.Equal(10001, exact.AwardedMinorUnits);
    }

    [Fact]
    public void FamilyCancellation_AtOrAfterStart_RefundsNothing()
    {
        BookingRefundDecision atStart = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(ScheduledStartUtc, captured: 40000));

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, atStart.RefundEntitlement);
        Assert.Equal(0, atStart.AwardedMinorUnits);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow, atStart.RefundDecisionReason);

        BookingRefundDecision afterStart = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(ScheduledStartUtc.AddMinutes(1), captured: 40000));

        Assert.Equal(0, afterStart.AwardedMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Executive clause: cancelling while unpaid, or before acceptance.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UnpaidCancellation_RefundsNothingAndNeverTouchesAcceptance()
    {
        Booking booking = NewBooking(revision: 2);
        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            ScheduledStartUtc.AddHours(-1));

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
        Assert.Equal(0, decision.AwardedMinorUnits);
        Assert.Null(decision.OutcomeReason);
    }

    [Fact]
    public void FamilyCancellation_BeforeAcceptance_RefundsTheWholeCapturedAmount()
    {
        Booking booking = NewBooking(revision: 2);
        booking.RecordPaymentIntent("order-1", PaymentMethod.Card, AcceptedAtUtc.AddHours(-1.5));
        booking.MarkAsPaid("order-1", "txn-1", AcceptedAtUtc.AddHours(-1));

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            AcceptedAtUtc);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance, decision.RefundDecisionReason);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void CaregiverRejection_BeforeAcceptance_RefundsTheWholeCapturedAmount()
    {
        Booking booking = PaidAwaitingApproval(revision: 2);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForCaregiverRejection(booking, AcceptedAtUtc));

        Assert.Equal(BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance, decision.RefundDecisionReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void CaregiverCancellation_AfterAcceptance_RefundsTheWholeCapturedAmount()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForCaregiverCancellation(booking, TwoHoursBefore, AcceptedReason()));

        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, decision.RefundDecisionReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Clause 3 and decision #5: multi-session discounted allocation.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AllocatePackageSessions_SumExactlyToThePaidTotal_WithRemainderOnTheFirstSessions()
    {
        IReadOnlyList<BookingSessionAllocation> allocations =
            BookingRefundPolicy.AllocatePackageSessions(paidPackageMinorUnits: 100000, sessionCount: 3);

        Assert.Equal(3, allocations.Count);
        Assert.Equal(33334, allocations[0].AllocatedMinorUnits);
        Assert.Equal(33333, allocations[1].AllocatedMinorUnits);
        Assert.Equal(33333, allocations[2].AllocatedMinorUnits);
        Assert.Equal(100000, allocations.Sum(a => a.AllocatedMinorUnits));
        Assert.Equal([1, 2, 3], allocations.Select(a => a.SessionIndex));
    }

    [Fact]
    public void MultiSessionCancellation_RefundsUnusedSessionsAtTheirAllocatedDiscountedPrice()
    {
        Booking booking = ConfirmedPackageBooking(revision: 2, paidPackageMinorUnits: 100000, sessionCount: 3);
        booking.RecordSessionDelivery(sessionIndex: 1, ScheduledStartUtc.AddHours(-30), AcceptedAtUtc);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            AcceptedAtUtc,
            AcceptedReason(),
            allocations: booking.SessionAllocations);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.True(decision.IsDeliveredSessionNonRefundable);
        Assert.Equal(66666, decision.AwardedMinorUnits);
        Assert.Equal(2, decision.RefundedSessions.Count);
    }

    [Fact]
    public void MultiSessionCancellation_InsideTheWindow_RefundsHalfOfEachUnusedAllocation()
    {
        Booking booking = ConfirmedPackageBooking(revision: 2, paidPackageMinorUnits: 100000, sessionCount: 3);

        // Nothing delivered; two sessions remain inside the 24-hour window.
        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            allocations: booking.SessionAllocations);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        // 33334/2 = 16667 exactly; 33333/2 = 16666.5 → half-to-even 16666.
        Assert.Equal(16667 + 16666 + 16666, decision.AwardedMinorUnits);
    }

    [Fact]
    public void MultiSessionCancellation_ProviderFailedSession_RefundsThatSessionInFull()
    {
        Booking booking = ConfirmedPackageBooking(revision: 2, paidPackageMinorUnits: 100000, sessionCount: 3);
        booking.RecordSessionProviderFailure(sessionIndex: 2, AcceptedAtUtc);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            allocations: booking.SessionAllocations);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        // Session 2 is provider-failed → full 33333; sessions 1 and 3 are inside the window → halves.
        Assert.Equal(16667 + 33333 + 16666, decision.AwardedMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Clause 4: provider failure, lateness, access, safety and force majeure.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ProviderLateness_Exactly60MinutesWithoutConsent_DoesNotTriggerTheFullRefund()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            providerLatenessMinutes: 60);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Null(decision.OutcomeReason);
        Assert.Equal(20000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ProviderLateness_61MinutesWithoutConsent_RefundsTheSessionInFull()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            providerLatenessMinutes: 61);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(BookingRefundOutcomeReason.ProviderLateBeyondThreshold, decision.OutcomeReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ProviderLateness_61MinutesWithCustomerConsent_DoesNotTriggerTheFullRefund()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            providerLatenessMinutes: 61,
            customerConsentedToLateness: true);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Null(decision.OutcomeReason);
        Assert.Equal(20000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void RecipientInaccessible_RefundsNothing()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            recipientWasInaccessible: true);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(0, decision.AwardedMinorUnits);
        Assert.Equal(BookingRefundOutcomeReason.RecipientInaccessible, decision.OutcomeReason);
    }

    [Fact]
    public void ForceMajeure_IsResolvedBeforeLatenessAndRefundsTheUndeliveredService()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            providerLatenessMinutes: 200,
            forceMajeure: true);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(BookingRefundOutcomeReason.ForceMajeure, decision.OutcomeReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ExplicitProviderCancelledOutcome_RefundsTheAffectedSessionInFull()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForOutcome(
            booking,
            TwoHoursBefore,
            AcceptedReason());

        BookingRefundDecision decision = BookingRefundPolicy.DecideOutcome(
            input,
            BookingRefundOutcomeReason.ProviderCancelled);

        Assert.Equal(BookingRefundOutcomeReason.ProviderCancelled, decision.OutcomeReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ARecordedOutcome_ForcesAFullAwardEvenForACaregiverActorPath()
    {
        // MAJ-3: the explicit outcome must be resolved BEFORE the caregiver-actor shortcut, or an
        // outcome endpoint would fall into the "caregiver cancelled, refund everything" branch and
        // the clause-4 reason would never reach the package path.
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForOutcome(
            booking,
            TwoHoursBefore,
            BookingCancellationFeedback.Create(
                BookingCancellationReasonCategory.Other,
                "provider did not arrive"));

        BookingRefundDecision decision = BookingRefundPolicy.DecideOutcome(
            input,
            BookingRefundOutcomeReason.ProviderNoShow);

        Assert.Equal(BookingRefundOutcomeReason.ProviderNoShow, decision.OutcomeReason);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, decision.RefundDecisionReason);
        Assert.Equal(40000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ARecordedOutcome_OnAPackage_KeepsDeliveredSessionsPayableAndReachesTheSessionPath()
    {
        // MAJ-3 with clause 3: with the forced outcome resolved first, a provider no-show on a
        // partly delivered package refunds the undelivered sessions only — the delivered session
        // stays payable instead of the whole booking being refunded by the actor shortcut.
        Booking booking = ConfirmedPackageBooking(revision: 2, paidPackageMinorUnits: 40000, sessionCount: 2);
        booking.RecordSessionDelivery(
            sessionIndex: 1,
            deliveredOnUtc: TwoHoursBefore.AddHours(-1),
            utcNow: TwoHoursBefore);

        BookingRefundDecision decision = BookingRefundPolicy.DecideOutcome(
            BookingRefundPolicyInput.ForOutcome(
                booking,
                TwoHoursBefore,
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Other,
                    "provider did not arrive"),
                allocations: booking.SessionAllocations),
            BookingRefundOutcomeReason.ProviderNoShow);

        Assert.True(decision.IsDeliveredSessionNonRefundable);
        Assert.Equal(20000, decision.AwardedMinorUnits);
        Assert.Single(decision.RefundedSessions);
        Assert.Equal(2, decision.RefundedSessions[0].SessionIndex);
    }

    // ---------------------------------------------------------------------------------------------
    // Decision #4 ceiling and #31 floor.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RefundableCeiling_IsCapturedLessPreviouslyRefunded_AndClampsTheAward()
    {
        Booking booking = ConfirmedBooking(revision: 2);
        booking.RecordRefundedAmount(30000, AcceptedAtUtc);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            allocations: null);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(10000, decision.RefundableCeilingMinorUnits);
        Assert.Equal(10000, decision.AwardedProrationBasisUnits);
        Assert.Equal(5000, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ProrationBasis_IsHalfOfTheRemainingAmount_NotHalfOfTheCapturedAmount()
    {
        // Mastermind ruling on MAJ-7: the basis the code prorates is the REMAINING refundable
        // amount after previous refunds, and the 50% share is taken from that. This is the
        // #4-consistent reading — the ceiling is what may still be awarded — and it is the
        // behaviour the code has; the test pins the code, it does not redefine the policy.
        Booking booking = ConfirmedBooking(revision: 2);
        booking.RecordRefundedAmount(30000, AcceptedAtUtc);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                TwoHoursBefore,
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "family emergency")));

        // Remaining ceiling is 10000, so the 50% share is 5000 — not 50% of the original 40000.
        Assert.Equal(10000, decision.RefundableCeilingMinorUnits);
        Assert.Equal(10000, decision.AwardedProrationBasisUnits);
        Assert.Equal(5000, decision.AwardedMinorUnits);
        Assert.Equal(BookingRefundEntitlement.PartialRefund, decision.RefundEntitlement);
    }

    [Fact]
    public void AFullRefundAtTheBoundary_IsAFullEntitlement_NotAPartialOne()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                ScheduledStartUtc - TimeSpan.FromHours(24),
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "family emergency")));

        Assert.Equal(40000, decision.AwardedMinorUnits);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
    }

    [Fact]
    public void AFiftyPercentAward_IsAPartialEntitlement_NotAFullOne()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                TwoHoursBefore,
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "family emergency")));

        Assert.Equal(20000, decision.AwardedMinorUnits);
        Assert.Equal(40000, decision.AwardedProrationBasisUnits);

        // MAJ-6: the wire must not label a 50% award as a full refund.
        Assert.Equal(BookingRefundEntitlement.PartialRefund, decision.RefundEntitlement);
    }

    [Fact]
    public void RefundableCeiling_WhenEverythingWasAlreadyRefunded_IsZero()
    {
        Booking booking = ConfirmedBooking(revision: 2);
        booking.RecordRefundedAmount(40000, AcceptedAtUtc);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason());

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.Equal(0, decision.RefundableCeilingMinorUnits);
        Assert.Equal(0, decision.AwardedMinorUnits);
    }

    [Fact]
    public void ZeroCapturedAmount_ProducesAZeroAwardThatIsRecordedNotTreatedAsAnError()
    {
        Booking booking = NewBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason());

        BookingRefundDecision decision = BookingRefundPolicy.Decide(input);

        Assert.True(decision.IsZeroAward);
        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
    }

    // ---------------------------------------------------------------------------------------------
    // Guards: the policy refuses rather than guesses.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConfirmedBookingWithoutScheduledStart_IsRefused()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        var input = new BookingRefundPolicyInput(
            BookingStatus.Confirmed,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            ScheduledStartUtc: null,
            TwoHoursBefore,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            BookingCaptureEvidence.Captured,
            CapturedMinorUnits: 40000,
            PreviouslyRefundedMinorUnits: 0,
            Allocations: null,
            ProviderLatenessMinutes: null,
            CustomerConsentedToLateness: false,
            RecipientWasInaccessible: false,
            Feedback: AcceptedReason());

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    [Fact]
    public void NonUtcTimestamp_IsRefused()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            DateTime.SpecifyKind(TwoHoursBefore, DateTimeKind.Unspecified),
            AcceptedReason());

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    [Fact]
    public void ConfirmedBookingMissingRequiredFeedback_IsRefused()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            feedback: null);

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    [Fact]
    public void AlreadyEndedBooking_IsRefusedRatherThanRepriced()
    {
        Booking booking = ConfirmedBooking(revision: 2);
        booking.CancelByFamily("done", TwoHoursBefore);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason());

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    [Fact]
    public void NonDenseSessionAllocations_AreRefused()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        var gapped = new List<BookingSessionAllocation>
        {
            BookingSessionAllocation.FromPersisted(2, 33333, BookingSessionOutcome.NotDelivered, null),
            BookingSessionAllocation.FromPersisted(3, 33333, BookingSessionOutcome.NotDelivered, null)
        };

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            allocations: gapped);

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    [Fact]
    public void InaccessibleRecipientAndConsentedLateness_AreContradictoryAndRefused()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        BookingRefundPolicyInput input = BookingRefundPolicyInput.ForFamilyCancellation(
            booking,
            TwoHoursBefore,
            AcceptedReason(),
            customerConsentedToLateness: true,
            recipientWasInaccessible: true);

        Assert.Throws<DomainException>(() => BookingRefundPolicy.Decide(input));
    }

    // ---------------------------------------------------------------------------------------------
    // Clause 5 and clause 8 helpers.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComplaintWindow_IsInclusiveAtExactly48HoursAndRefusesARetroactiveReport()
    {
        DateTime visited = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(BookingRefundPolicy.IsComplaintWithinReportingWindow(visited, visited.AddHours(48)));
        Assert.False(BookingRefundPolicy.IsComplaintWithinReportingWindow(visited, visited.AddHours(48).AddTicks(1)));
        Assert.Throws<DomainException>(
            () => BookingRefundPolicy.IsComplaintWithinReportingWindow(visited, visited.AddMinutes(-1)));
    }

    [Fact]
    public void Reschedule_IsFreeAtExactly24HoursBeforeStart()
    {
        Assert.True(BookingRefundPolicy.IsRescheduleFreeOfCharge(ScheduledStartUtc, Exactly24HoursBefore));
        Assert.False(BookingRefundPolicy.IsRescheduleFreeOfCharge(ScheduledStartUtc, Exactly24HoursBefore.AddTicks(1)));
    }

    // ---------------------------------------------------------------------------------------------
    // Revision preservation: the decision carries the acting booking's revision.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DecisionRecordsTheScheduledStartRevision()
    {
        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            ConfirmedFamilyInput(TwoHoursBefore, captured: 40000));

        Assert.True(decision.PolicyVersion.IsScheduledStart);
        Assert.Equal(
            BookingRefundPolicyVersion.PolicyDocumentEffectiveOn,
            decision.PolicyVersion.DocumentEffectiveOnUtc);
    }

    // ---------------------------------------------------------------------------------------------
    // Dispatch: legacy bookings keep the v1 acceptance-relative rule.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Dispatch_LegacyBooking_UsesTheAcceptanceRelativePolicy()
    {
        Booking booking = ConfirmedBooking(revision: 1);

        (BookingRefundDecision? scheduled, BookingCancellationDecision? legacy) = BookingRefundPolicyDispatch.Decide(
            booking,
            BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                TwoHoursBefore,
                AcceptedReason()),
            BookingRefundPolicyInput.ForFamilyCancellation(booking, TwoHoursBefore, AcceptedReason()));

        Assert.Null(scheduled);
        Assert.NotNull(legacy);
    }

    [Fact]
    public void Dispatch_ScheduledStartBooking_UsesTheRevisionTwoPolicy()
    {
        Booking booking = ConfirmedBooking(revision: 2);

        (BookingRefundDecision? scheduled, BookingCancellationDecision? legacy) = BookingRefundPolicyDispatch.Decide(
            booking,
            BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                TwoHoursBefore,
                AcceptedReason()),
            BookingRefundPolicyInput.ForFamilyCancellation(booking, TwoHoursBefore, AcceptedReason()));

        Assert.NotNull(scheduled);
        Assert.Null(legacy);
        Assert.True(BookingRefundPolicyDispatch.UsesScheduledStartPolicy(booking));
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static BookingRefundPolicyInput ConfirmedFamilyInput(
        DateTime actedOnUtc,
        long captured) =>
        BookingRefundPolicyInput.ForFamilyCancellation(
            ConfirmedBooking(revision: 2, captured: captured / 100m),
            actedOnUtc,
            AcceptedReason());

    private static BookingCancellationFeedback AcceptedReason() =>
        BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Emergency, "family emergency");

    private static Booking NewBooking(int revision, decimal captured = 400m) =>
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
            BookingPriceSnapshot.Calculate(captured, 0m, 0m, 1),
            acceptanceDeadlineUtc: ScheduledStartUtc.AddHours(-1),
            currentDate: DateOnly.FromDateTime(AcceptedAtUtc),
            createdOnUtc: AcceptedAtUtc.AddHours(-1),
            refundPolicyVersion: revision == 2
                ? BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: true)
                : BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: false));

    private static Booking PaidAwaitingApproval(int revision)
    {
        Booking booking = NewBooking(revision);

        // MAJ-7: a succeeded transaction is what makes capture evidence resolvable. Without the
        // recorded intent, ResolveCaptureEvidence reports Ambiguous and the policy correctly
        // refuses to decide, so the fixture must record the intent before the payment.
        booking.RecordPaymentIntent("order", PaymentMethod.Card, AcceptedAtUtc.AddMinutes(-31));
        booking.MarkAsPaid("order", "txn", AcceptedAtUtc.AddMinutes(-30));
        return booking;
    }

    private static Booking ConfirmedBooking(int revision, decimal captured = 400m)
    {
        Booking booking = NewBooking(revision, captured);

        booking.RecordPaymentIntent("order", PaymentMethod.Card, AcceptedAtUtc.AddMinutes(-31));
        booking.MarkAsPaid("order", "txn", AcceptedAtUtc.AddMinutes(-30));
        booking.AcceptByCaregiver(AcceptedAtUtc);
        return booking;
    }

    private static Booking ConfirmedPackageBooking(int revision, long paidPackageMinorUnits, int sessionCount)
    {
        Booking booking = NewBooking(revision, paidPackageMinorUnits / 100m);

        // Package sessions are allocated at purchase, before the booking is paid.
        booking.AttachSessionAllocations(paidPackageMinorUnits, sessionCount);
        booking.RecordPaymentIntent("order", PaymentMethod.Card, AcceptedAtUtc.AddMinutes(-31));
        booking.MarkAsPaid("order", "txn", AcceptedAtUtc.AddMinutes(-30));
        booking.AcceptByCaregiver(AcceptedAtUtc);
        return booking;
    }
}
