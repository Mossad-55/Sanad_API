using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Aggregate-level dispatch for the care-home refund policy (W3-F2): revision-1
/// bookings keep their original amounts byte-for-byte (AwayFromZero included),
/// revision-2 bookings decide from their own snapshot (facility terms or the
/// clause-2 default), a recorded check-in makes a revision-2 family
/// cancellation refund nothing (delivered service), zero-award cancellations
/// leave the paid money with the facility outside the refund lifecycle and
/// OUT of the payout projection (allocation is an Owner decision), and
/// extension segments snapshot the terms applicable at their own purchase
/// (decisions #4, #8, #10, #31). DST facts pin the scheduled-start resolution
/// across Cairo's midnight transitions.
/// </summary>
public sealed class CareHomeRefundPolicyBookingTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static long _transactionId;

    private static CareHomePolicySnapshot V2(string? termsJson) =>
        CareHomePolicySnapshot.From(CareHomeRefundPolicyVersion.ForNewBooking(true), null, termsJson);

    private static CareHomeBooking NewBooking(
        decimal baseAmount, decimal fee, decimal tax, DateOnly start, CareHomePolicySnapshot? snapshot)
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            new CareHomeId(Guid.NewGuid()), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            start, baseAmount, fee, tax, 1, "ع", "Elderly", 75, null, "Contact", null, null, null, Now,
            policySnapshot: snapshot);
        booking.MarkPaid(++_transactionId, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        return booking;
    }

    [Fact]
    public void Legacy_bookings_refund_full_before_check_in_and_half_after_with_away_from_zero()
    {
        // total 100.01: half is 50.005 — AwayFromZero rounds to 50.01 where the
        // revision-2 banker's rule would round to 50.00.
        var beforeCheckIn = NewBooking(90.01m, 5m, 5m, new DateOnly(2026, 10, 10), snapshot: null);
        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, beforeCheckIn.RefundPolicyVersion);
        Assert.Equal(default, beforeCheckIn.RefundPolicyDocumentEffectiveOn);
        Assert.Null(beforeCheckIn.CancellationTermsSnapshotJson);

        Assert.Equal(100.01m, beforeCheckIn.CancelByFamily(Now.AddMinutes(5)));
        Assert.Equal(CareHomeBookingStatus.RefundPending, beforeCheckIn.Status);
        Assert.Equal(CareHomeRefundStatus.Pending, beforeCheckIn.RefundStatus);
        Assert.Equal(100.01m, beforeCheckIn.RefundAmount);

        var afterCheckIn = NewBooking(90.01m, 5m, 5m, new DateOnly(2026, 10, 10), snapshot: null);
        afterCheckIn.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        afterCheckIn.RecordCheckIn(UserId.New(), Now.AddMinutes(4));

        Assert.Equal(50.01m, afterCheckIn.CancelByFamily(Now.AddMinutes(5)));
        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, afterCheckIn.RefundPolicyVersion);
    }

    [Fact]
    public void Revision2_bookings_without_terms_follow_the_clause2_default_schedule()
    {
        DateOnly start = new(2026, 10, 10);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(start);

        var full = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(CareHomeRefundPolicyVersion.ScheduledStartVersion, full.RefundPolicyVersion);
        Assert.Equal(CareHomeRefundPolicyVersion.PolicyDocumentEffectiveOn, full.RefundPolicyDocumentEffectiveOn);
        Assert.Null(full.CancellationTermsSnapshotJson);
        Assert.Equal(13230m, full.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow));
        Assert.Equal(13230m, full.RefundAmount);

        var half = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(6615.00m, half.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(1)));
        Assert.Equal(CareHomeBookingStatus.RefundPending, half.Status);
        Assert.Equal(6615.00m, half.RefundAmount);

        // Revision-2 delivered-service guard (clauses 2 and 3): a recorded
        // check-in means care was delivered, so a family cancellation refunds
        // nothing wherever it sits on the window. Revision 1 keeps its
        // original half-after-check-in rule byte-for-byte (the legacy fact).
        var delivered = NewBooking(12000m, 600m, 630m, start, V2(null));
        delivered.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        delivered.RecordCheckIn(UserId.New(), Now.AddMinutes(4));
        Assert.Equal(0m, delivered.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(1)));
        Assert.Equal(CareHomeBookingStatus.Cancelled, delivered.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, delivered.PaymentStatus);
        Assert.Equal(CareHomeRefundStatus.None, delivered.RefundStatus);
        Assert.Null(delivered.RefundAmount);
        Assert.Equal("Cancelled by Family.", delivered.DecisionReason);

        var none = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(0m, none.CancelByFamily(scheduledStart));
        Assert.Equal(CareHomeBookingStatus.Cancelled, none.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, none.PaymentStatus);
        Assert.Equal(CareHomeRefundStatus.None, none.RefundStatus);
        Assert.Null(none.RefundAmount);
        Assert.Equal("Cancelled by Family.", none.DecisionReason);
    }

    [Fact]
    public void Revision2_bookings_with_terms_follow_the_first_satisfied_tier()
    {
        DateOnly start = new(2026, 10, 10);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(start);
        string termsJson = CareHomeCancellationTerms.From(
        [
            new CareHomeCancellationTermTier(50, 12),
            new CareHomeCancellationTermTier(100, 48),
        ]).ToJson();

        var full = NewBooking(12000m, 600m, 630m, start, V2(termsJson));
        Assert.Equal(termsJson, full.CancellationTermsSnapshotJson);
        Assert.Equal(13230m, full.CancelByFamily(scheduledStart - TimeSpan.FromHours(48)));

        var half = NewBooking(12000m, 600m, 630m, start, V2(termsJson));
        Assert.Equal(6615.00m, half.CancelByFamily(scheduledStart - TimeSpan.FromHours(47)));

        var none = NewBooking(12000m, 600m, 630m, start, V2(termsJson));
        Assert.Equal(0m, none.CancelByFamily(scheduledStart - TimeSpan.FromHours(11)));
        Assert.Equal(CareHomeBookingStatus.Cancelled, none.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, none.PaymentStatus);
        Assert.Equal(CareHomeRefundStatus.None, none.RefundStatus);
        Assert.Null(none.RefundAmount);
    }

    [Fact]
    public void Payout_eligibility_stays_behind_pre_policy_rules_for_zero_award_and_refund_without_stay()
    {
        DateOnly start = new(2026, 10, 10);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(start);

        // A zero-award cancellation never entered the refund lifecycle and has
        // no recorded checkout: it is NOT payout-eligible. The pre-policy rule
        // (checkout-completed stays only) is unchanged by this lane; how the
        // money a zero-award cancellation retains is allocated stays an
        // explicit Owner decision, so the projection must not improvise it.
        var zeroAward = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(0m, zeroAward.CancelByFamily(scheduledStart));
        Assert.False(CareHomePayoutCalculation.TryGetRemainingAmounts(zeroAward, out _, out _));

        // A refund completed without any stay (the clause-2 50% share of a
        // late cancellation) keeps its retained share with the facility but is
        // NOT payout-eligible either: eligibility still requires a recorded
        // checkout. The pre-existing facts in CareHomePayoutTests continue to
        // pin the eligible checkout-completed shapes in both directions, so a
        // retrospective widening of payout eligibility fails one of these pins.
        var refundedEarly = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(6615.00m, refundedEarly.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(1)));
        Assert.True(refundedEarly.TryClaimRefund(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(2)));
        refundedEarly.MarkRefundInitiated("chbr-unit-refund", scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(3));
        Assert.True(refundedEarly.MarkRefundCompleted("chbr-unit-refund", scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(4)));

        Assert.Equal(CareHomeBookingStatus.Refunded, refundedEarly.Status);
        Assert.Null(refundedEarly.ActualCheckOutOnUtc);
        Assert.False(CareHomePayoutCalculation.TryGetRemainingAmounts(refundedEarly, out _, out _));
    }

    [Fact]
    public void Extension_segments_snapshot_their_own_terms_and_act_against_their_own_scheduled_start()
    {
        DateOnly start = new(2026, 10, 10);
        string rootTermsJson = CareHomeCancellationTerms.From([new CareHomeCancellationTermTier(50, 12)]).ToJson();
        string extensionTermsJson = CareHomeCancellationTerms.From(
        [
            new CareHomeCancellationTermTier(50, 12),
            new CareHomeCancellationTermTier(100, 48),
        ]).ToJson();

        CareHomeBooking root = NewBooking(12000m, 600m, 630m, start, V2(rootTermsJson));
        CareHomeBooking extension = CareHomeBooking.CreateExtension(
            root, UserId.New(), 12000m, 600m, 630m, 1, Now.AddMinutes(10), policySnapshot: V2(extensionTermsJson));
        extension.MarkPaid(++_transactionId, Now.AddMinutes(11));
        extension.Accept(Now.AddMinutes(12));

        // Decision #8: the extension keeps the terms snapped at its own purchase,
        // never the root segment's.
        Assert.Equal(root.EndDate, extension.StartDate);
        Assert.Equal(CareHomeRefundPolicyVersion.ScheduledStartVersion, extension.RefundPolicyVersion);
        Assert.Equal(extensionTermsJson, extension.CancellationTermsSnapshotJson);
        Assert.NotEqual(root.CancellationTermsSnapshotJson, extension.CancellationTermsSnapshotJson);

        // Provider failure: the extension is refunded in full whatever its terms.
        CareHomeBooking providerEnded = CareHomeBooking.CreateExtension(
            root, UserId.New(), 12000m, 600m, 630m, 1, Now.AddMinutes(10), policySnapshot: V2(extensionTermsJson));
        providerEnded.MarkPaid(++_transactionId, Now.AddMinutes(11));
        providerEnded.Accept(Now.AddMinutes(12));
        providerEnded.CancelUnusedExtension(root.EndDate.AddDays(-2), Now.AddMinutes(20), providerEnded: true);
        Assert.Equal(CareHomeBookingStatus.RefundPending, providerEnded.Status);
        Assert.Equal(13230m, providerEnded.RefundAmount);

        // Family-ended: the extension decides against its own scheduled start
        // using its own tiers. A day inside the 12-hour deadline → 50%.
        CareHomeBooking familyEnded = CareHomeBooking.CreateExtension(
            root, UserId.New(), 12000m, 600m, 630m, 1, Now.AddMinutes(10), policySnapshot: V2(extensionTermsJson));
        familyEnded.MarkPaid(++_transactionId, Now.AddMinutes(11));
        familyEnded.Accept(Now.AddMinutes(12));
        DateTime extensionStart = CareHomeStayStart.ScheduledStartUtc(familyEnded.StartDate);
        familyEnded.CancelUnusedExtension(root.EndDate.AddDays(-2), extensionStart - TimeSpan.FromHours(12), providerEnded: false);
        Assert.Equal(CareHomeBookingStatus.RefundPending, familyEnded.Status);
        Assert.Equal(6615.00m, familyEnded.RefundAmount);

        // Family-ended below every tier: a zero award is a valid outcome.
        CareHomeBooking zeroAward = CareHomeBooking.CreateExtension(
            root, UserId.New(), 12000m, 600m, 630m, 1, Now.AddMinutes(10), policySnapshot: V2(extensionTermsJson));
        zeroAward.MarkPaid(++_transactionId, Now.AddMinutes(11));
        zeroAward.Accept(Now.AddMinutes(12));
        zeroAward.CancelUnusedExtension(root.EndDate.AddDays(-2), extensionStart - TimeSpan.FromHours(11), providerEnded: false);
        Assert.Equal(CareHomeBookingStatus.Cancelled, zeroAward.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, zeroAward.PaymentStatus);
        Assert.Equal(CareHomeRefundStatus.None, zeroAward.RefundStatus);
        Assert.Null(zeroAward.RefundAmount);
        Assert.Equal("The existing stay ended before this extension period began.", zeroAward.DecisionReason);

        // Revision-1 extensions keep their original full-amount behavior: a
        // legacy root's extension refunds the whole segment whatever the family
        // timing.
        CareHomeBooking legacyRoot = NewBooking(12000m, 600m, 630m, start, snapshot: null);
        CareHomeBooking legacy = CareHomeBooking.CreateExtension(
            legacyRoot, UserId.New(), 12000m, 600m, 630m, 1, Now.AddMinutes(10));
        legacy.MarkPaid(++_transactionId, Now.AddMinutes(11));
        legacy.Accept(Now.AddMinutes(12));
        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, legacy.RefundPolicyVersion);
        legacy.CancelUnusedExtension(start.AddDays(28), extensionStart - TimeSpan.FromHours(11), providerEnded: false);
        Assert.Equal(CareHomeBookingStatus.RefundPending, legacy.Status);
        Assert.Equal(13230m, legacy.RefundAmount);
    }

    [Fact]
    public void Scheduled_start_resolves_a_spring_gap_midnight_deterministically()
    {
        // 2026-04-24: Egypt's spring transition lands on midnight, so the first
        // wall-clock reading of the day can fall inside the transition gap. The
        // scheduled start must still resolve — deterministically, to the first
        // instant that actually existed (the BusinessDayCalendar pattern) —
        // instead of throwing or silently drifting off the start date.
        TimeZoneInfo cairo = CairoZone();
        DateTime startUtc = CareHomeStayStart.ScheduledStartUtc(new DateOnly(2026, 4, 24));

        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
        DateTime backToLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, cairo);
        Assert.False(cairo.IsInvalidTime(backToLocal));
        Assert.Equal(new DateOnly(2026, 4, 24), DateOnly.FromDateTime(backToLocal));
        Assert.Equal(startUtc, CareHomeStayStart.ScheduledStartUtc(new DateOnly(2026, 4, 24)));
    }

    [Fact]
    public void Scheduled_start_on_a_fall_back_midnight_is_deterministic()
    {
        // 2026-10-29: the autumn transition also lands on midnight, so 00:00
        // occurs twice. The scheduled start resolves deterministically (the
        // zone's standard reading) and the window boundaries hold against it.
        DateOnly start = new(2026, 10, 29);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(start);

        Assert.Equal(DateTimeKind.Utc, scheduledStart.Kind);
        Assert.Equal(scheduledStart, CareHomeStayStart.ScheduledStartUtc(start));

        var full = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(13230m, full.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow));

        var half = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(6615.00m, half.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Exact_window_boundaries_hold_on_a_spring_gap_scheduled_start()
    {
        // The reviewer's boundary pins, measured against the DST-affected
        // scheduled start itself: exactly 24 hours before it is still a full
        // refund, one minute inside is half, and at/after it is nothing.
        // (The 2027 spring gap: the aggregate's creation guard requires the
        // stay to be at least 24 hours ahead, so a past date cannot be used.)
        DateOnly start = new(2027, 4, 30);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(start);

        var full = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(13230m, full.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow));
        Assert.Equal(13230m, full.RefundAmount);

        var half = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(6615.00m, half.CancelByFamily(scheduledStart - CareHomeRefundPolicy.FullRefundWindow + TimeSpan.FromMinutes(1)));

        var none = NewBooking(12000m, 600m, 630m, start, V2(null));
        Assert.Equal(0m, none.CancelByFamily(scheduledStart));
    }

    private static TimeZoneInfo CairoZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }
}
