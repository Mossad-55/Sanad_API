using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.UnitTests.Rentals;

/// <summary>
/// W3-F4 clause 8 domain behavior: the whole-day accounting (#19), the
/// seven-business-day deposit window and its deduction netting (#20, #21),
/// the crash-safe refund claim discipline and the captured-funds ceiling
/// (#4). Authored by hand and marked NOT RUN: executing them is the
/// Mastermind's build/test slot, never a lane step.
/// </summary>
public sealed class RentalBookingDomainTests
{
    /// <summary>Piastres: rate 120.00 EGP/day, deposit 500.00 EGP.</summary>
    private const long DailyRate = 12_000;
    private const long DepositAmount = 50_000;

    private static readonly Guid FamilyId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static RentalEquipment NewEquipment() =>
        RentalEquipment.Create("Testing rig", "منتج اختبار", null, null, DailyRate, DepositAmount, 30, Utc(1, 9));

    private static RentalBooking NewBooking(int plannedDays, DateTime createdOnUtc)
    {
        RentalBooking booking = RentalBooking.Create(NewEquipment(), FamilyId, UserId, plannedDays, null, createdOnUtc);
        RentalPaymentTransaction transaction = booking.OpenPaymentTransaction("rnt_test", RentalPaymentMethod.Card, createdOnUtc);
        booking.ConfirmPayment(transaction, 424_242, createdOnUtc);
        return booking;
    }

    // ----- #19: days are 24-hour periods anchored to actual delivery -----

    [Fact]
    public void Early_return_refunds_whole_unused_prepaid_periods()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        // 30 hours elapsed: two started 24-hour periods count as used, so
        // three whole prepaid periods refund at the snapshotted rate.
        Assert.Equal(3 * DailyRate, booking.ComputeEarlyReturnUnusedDaysAmount());
    }

    [Fact]
    public void A_period_that_just_started_counts_as_used()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        // 24 hours + one minute: the third period has started, so only two.
        booking.RecordReturn(Utc(2, 8).AddHours(24).AddMinutes(1), Utc(13, 8), AdminId);

        Assert.Equal(3 * DailyRate, booking.ComputeEarlyReturnUnusedDaysAmount());
    }

    [Fact]
    public void A_return_exactly_at_the_24_hour_mark_leaves_four_unused_periods()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(2, 8).AddHours(24), Utc(13, 8), AdminId);

        Assert.Equal(4 * DailyRate, booking.ComputeEarlyReturnUnusedDaysAmount());
    }

    [Fact]
    public void A_return_without_measurable_elapsed_time_refunds_every_prepaid_period()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        DateTime delivered = Utc(2, 8);
        booking.RecordDelivery(delivered, AdminId);
        booking.RecordReturn(delivered, Utc(13, 8), AdminId);

        Assert.Equal(5 * DailyRate, booking.ComputeEarlyReturnUnusedDaysAmount());
    }

    [Fact]
    public void A_late_return_beyond_the_prepaid_horizon_refunds_nothing()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        // Six days later: every prepaid period is used, nothing is owed.
        booking.RecordReturn(Utc(8, 8), Utc(15, 8), AdminId);

        Assert.Equal(0, booking.ComputeEarlyReturnUnusedDaysAmount());
    }

    // ----- #20/#21: the deposit window and deduction netting -----

    [Fact]
    public void Settlement_nets_only_deductions_approved_on_or_before_the_deadline()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        RentalDamageDeduction onTime = booking.AddDamageDeduction("Scratched panel", "خدش", 10_000, "evidence/on-time", AdminId, Utc(3, 15));
        onTime.Approve(AdminId, Utc(4, 8));

        RentalDamageDeduction unapproved = booking.AddDamageDeduction("Missing cap", null, 7_000, "evidence/unapproved", AdminId, Utc(3, 16));
        // A second deduction left unapproved never nets.

        RentalDamageDeduction late = booking.AddDamageDeduction("Late-found dent", null, 5_000, "evidence/late", AdminId, Utc(3, 17));
        late.Approve(AdminId, Utc(14, 8)); // approved after the deadline

        Assert.Equal(DepositAmount - 10_000, booking.ComputeDepositSettlementAmount(Utc(12, 8)));
    }

    [Fact]
    public void Settlement_after_the_deadline_releases_the_deposit_in_full()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        RentalDamageDeduction deduction = booking.AddDamageDeduction("Scratched panel", "خدش", 10_000, "evidence/late-approved", AdminId, Utc(3, 15));
        deduction.Approve(AdminId, Utc(14, 8)); // approved after the deadline

        // A settle after the deadline releases the deposit in full, whatever
        // the recorded evidence: the hold window cannot be extended (#20).
        Assert.Equal(DepositAmount, booking.ComputeDepositSettlementAmount(Utc(13, 9)));
    }

    [Fact]
    public void Deductions_beyond_the_deposit_release_the_full_deposit_and_never_go_negative()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        RentalDamageDeduction first = booking.AddDamageDeduction("Broken hinge", null, 40_000, "evidence/a", AdminId, Utc(3, 15));
        first.Approve(AdminId, Utc(4, 8));
        RentalDamageDeduction second = booking.AddDamageDeduction("Cracked lid", null, 40_000, "evidence/b", AdminId, Utc(3, 15));
        second.Approve(AdminId, Utc(4, 8));

        // The floor is zero (#31): excess deductions stay recorded claims.
        Assert.Equal(0, booking.ComputeDepositSettlementAmount(Utc(12, 8)));
    }

    // ----- the refund claim discipline -----

    [Fact]
    public void A_cancellation_before_delivery_refunds_everything_captured_through_the_claimed_case()
    {
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        long capturedTotal = 2 * DailyRate + DepositAmount;

        bool nothingToRefund = booking.CancelBeforeDelivery(UserId, Utc(1, 10));
        Assert.False(nothingToRefund);

        Guid refundCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, capturedTotal, refundCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 11));
        RentalRefundMovement movement = booking.CompleteRefund(
            RentalRefundDestination.OriginalPaymentMethod, UserId, "PROV-1", "evidence/cancel", Utc(1, 12));

        Assert.Equal(refundCaseId, movement.RefundCaseId);
        Assert.Equal(capturedTotal, movement.AmountMinorUnits);
        Assert.True(booking.HasRefundMovement(RentalRefundMovementKind.CancellationFullRefund));
        Assert.Null(booking.PendingRefundKind);
        Assert.Null(booking.PendingRefundCaseId);
    }

    [Fact]
    public void An_unpaid_booking_cancels_without_a_refund()
    {
        RentalBooking booking = RentalBooking.Create(NewEquipment(), FamilyId, UserId, 3, null, Utc(1, 9));

        Assert.True(booking.CancelBeforeDelivery(UserId, Utc(1, 10)));
        Assert.Equal(RentalBookingStatus.CancelledBeforeDelivery, booking.Status);
    }

    [Fact]
    public void A_zero_award_settlement_is_a_valid_recorded_movement()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        RentalDamageDeduction deduction = booking.AddDamageDeduction("Total loss", null, DepositAmount + 1, "evidence/total", AdminId, Utc(3, 15));
        deduction.Approve(AdminId, Utc(4, 8));

        long award = booking.ComputeDepositSettlementAmount(Utc(12, 8));
        Assert.Equal(0, award);

        Guid refundCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.DepositSettlement, award, refundCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(12, 8));
        RentalRefundMovement movement = booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, AdminId, null, "evidence/zero", Utc(12, 9));

        Assert.Equal(0, movement.AmountMinorUnits);
        Assert.Equal(refundCaseId, movement.RefundCaseId);
        booking.MarkDepositSettled(Utc(12, 10), AdminId);
        Assert.Equal(RentalBookingStatus.DepositSettled, booking.Status);
    }

    [Fact]
    public void A_claim_whose_amount_does_not_match_the_policy_derived_award_is_refused()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        Assert.Throws<DomainException>(() =>
            booking.BeginRefund(RentalRefundMovementKind.EarlyReturnUnusedDays, 3 * DailyRate + 1, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(4, 8)));
    }

    [Fact]
    public void A_second_refund_claim_refuses_while_one_is_in_flight_and_after_the_kind_is_recorded()
    {
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        booking.CancelBeforeDelivery(UserId, Utc(1, 10));
        long capturedTotal = 2 * DailyRate + DepositAmount;
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, capturedTotal, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 11));

        // While the claim is pending, a second claim of the same kind is refused.
        Assert.Throws<DomainException>(() =>
            booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, capturedTotal, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 12)));

        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, UserId, null, "evidence/first", Utc(1, 13));

        // Once the movement row landed, the kind is closed forever (the
        // outcome row doubles as the idempotency marker).
        Assert.Throws<DomainException>(() =>
            booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, capturedTotal, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 14)));
    }

    [Fact]
    public void Payment_confirmation_is_idempotent_and_never_captures_twice()
    {
        RentalBooking booking = RentalBooking.Create(NewEquipment(), FamilyId, UserId, 3, null, Utc(1, 9));
        RentalPaymentTransaction transaction = booking.OpenPaymentTransaction("rnt_test", RentalPaymentMethod.Card, Utc(1, 9));
        booking.ConfirmPayment(transaction, 777, Utc(1, 10));
        int versionAfterFirstConfirmation = booking.Version;

        // A late duplicate webhook confirm: idempotent no-op.
        booking.ConfirmPayment(transaction, 777, Utc(1, 15));

        Assert.Equal(RentalBookingStatus.Confirmed, booking.Status);
        Assert.Equal(3 * DailyRate + DepositAmount, booking.CapturedTotalMinorUnits);
        Assert.Equal(versionAfterFirstConfirmation, booking.Version);
    }

    [Fact]
    public void The_deduction_lifecycle_is_append_only_and_disputes_are_never_rewritten()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        RentalDamageDeduction deduction = booking.AddDamageDeduction("Scratched panel", "خدش", 10_000, "evidence/scratch", AdminId, Utc(3, 15));
        deduction.Approve(AdminId, Utc(4, 8));
        Assert.Throws<DomainException>(() => deduction.Approve(AdminId, Utc(4, 9)));

        deduction.RecordDispute(Utc(5, 8), "The scratch was pre-existing");
        Assert.Throws<DomainException>(() => deduction.RecordDispute(Utc(5, 9), "again"));
        Assert.Equal(RentalDamageDisputeState.Disputed, deduction.DisputeState);

        deduction.ResolveDispute(AdminId, Utc(6, 8));
        Assert.Equal(RentalDamageDisputeState.Resolved, deduction.DisputeState);
        Assert.Throws<DomainException>(() => deduction.ResolveDispute(AdminId, Utc(6, 9)));
        // Resolution never rewrote the recorded dispute facts.
        Assert.Equal("The scratch was pre-existing", deduction.DisputeReason);
        Assert.NotNull(deduction.DisputedOnUtc);
    }

    [Fact]
    public void A_completed_inspection_cannot_be_completed_twice_and_requires_a_returned_booking()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);

        Assert.Throws<DomainException>(() => booking.CompleteInspection(Utc(3, 8), AdminId, null));

        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);
        booking.CompleteInspection(Utc(4, 8), AdminId, "All good");
        Assert.Throws<DomainException>(() => booking.CompleteInspection(Utc(5, 8), AdminId, null));
    }

    [Fact]
    public void Non_utc_timestamps_are_refused_everywhere()
    {
        RentalEquipment equipment = NewEquipment();
        DateTime local = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<DomainException>(() => RentalEquipment.Create("x", "y", null, null, 100, 0, 5, local));
        Assert.Throws<DomainException>(() => RentalBooking.Create(equipment, FamilyId, UserId, 3, null, local));
        Assert.Throws<DomainException>(() => RentalPaymentTransaction.Create(Guid.CreateVersion7(), "rnt_x", RentalPaymentMethod.Card, 100, local));
        Assert.Throws<DomainException>(() => RentalDamageDeduction.Create(Guid.CreateVersion7(), "damage", null, 100, "evidence", AdminId, local));
        Assert.Throws<DomainException>(() => RentalRefundMovement.Create(Guid.CreateVersion7(), RentalRefundMovementKind.CancellationFullRefund, Guid.CreateVersion7(), RentalRefundDestination.AppCredit, 100, UserId, null, null, local));
    }

    [Fact]
    public void Deposit_settlement_requires_an_anchored_deadline()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 14), Utc(13, 8), AdminId);

        long award = booking.ComputeDepositSettlementAmount(Utc(12, 8));
        Guid refundCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.DepositSettlement, award, refundCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(12, 8));
        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, AdminId, "PROV-2", "evidence/settle", Utc(12, 9));
        booking.MarkDepositSettled(Utc(12, 10), AdminId);

        Assert.Equal(RentalBookingStatus.DepositSettled, booking.Status);
        Assert.Equal(Utc(13, 8), booking.SettlementDeadlineUtc);
    }

    // ----- M5: the deposit settlement and the unused-days refund are order-independent -----

    [Fact]
    public void Unused_days_refund_stays_available_after_the_deposit_is_settled()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 8), Utc(13, 8), AdminId); // exactly 24h: one used period

        long settlementAward = booking.ComputeDepositSettlementAmount(Utc(12, 8));
        booking.BeginRefund(RentalRefundMovementKind.DepositSettlement, settlementAward, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(12, 8));
        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, AdminId, "PROV-S1", "evidence/settle", Utc(12, 9));
        booking.MarkDepositSettled(Utc(12, 10), AdminId);

        // Settling the deposit first must not foreclose the unused-days refund.
        Assert.Equal(RentalBookingStatus.DepositSettled, booking.Status);

        booking.BeginRefund(RentalRefundMovementKind.EarlyReturnUnusedDays, 4 * DailyRate, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(12, 11));
        RentalRefundMovement movement = booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, UserId, null, "evidence/unused", Utc(12, 12));

        Assert.Equal(4 * DailyRate, movement.AmountMinorUnits);

        long totalRefunded = booking.RefundMovements.Sum(candidate => candidate.AmountMinorUnits);
        Assert.True(totalRefunded <= booking.CapturedTotalMinorUnits); // #4 cumulative ceiling holds in this order too
    }

    [Fact]
    public void Unused_days_first_then_deposit_settlement_lands_both_exactly_once()
    {
        RentalBooking booking = NewBooking(5, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 8), Utc(13, 8), AdminId);

        booking.BeginRefund(RentalRefundMovementKind.EarlyReturnUnusedDays, 4 * DailyRate, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(4, 8));
        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, UserId, null, "evidence/unused", Utc(4, 9));

        // The unused-days completion did not move the status — settlement remains possible.
        Assert.Equal(RentalBookingStatus.Returned, booking.Status);

        long settlementAward = booking.ComputeDepositSettlementAmount(Utc(12, 8));
        booking.BeginRefund(RentalRefundMovementKind.DepositSettlement, settlementAward, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(12, 8));
        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, AdminId, "PROV-S2", "evidence/settle", Utc(12, 9));
        booking.MarkDepositSettled(Utc(12, 10), AdminId);

        Assert.Equal(RentalBookingStatus.DepositSettled, booking.Status);
        Assert.True(booking.HasRefundMovement(RentalRefundMovementKind.EarlyReturnUnusedDays));
        Assert.True(booking.HasRefundMovement(RentalRefundMovementKind.DepositSettlement));

        long totalRefunded = booking.RefundMovements.Sum(candidate => candidate.AmountMinorUnits);
        Assert.True(totalRefunded <= booking.CapturedTotalMinorUnits); // #4 cumulative ceiling holds in this order too
    }

    // ----- Version discipline: every protected lifecycle mutation bumps the token -----

    [Fact]
    public void Every_protected_lifecycle_mutation_bumps_the_concurrency_version()
    {
        RentalBooking booking = RentalBooking.Create(NewEquipment(), FamilyId, UserId, 3, null, Utc(1, 9));
        int version = booking.Version;

        RentalPaymentTransaction transaction = booking.OpenPaymentTransaction("rnt_version", RentalPaymentMethod.Wallet, Utc(1, 10));
        Assert.Equal(++version, booking.Version); // mutates the aggregate (child intent): the version token serializes intent creation

        booking.ConfirmPayment(transaction, 777, Utc(1, 11));
        Assert.Equal(++version, booking.Version);

        booking.RecordDelivery(Utc(2, 8), AdminId);
        Assert.Equal(++version, booking.Version);

        booking.RecordReturn(Utc(3, 8), Utc(13, 8), AdminId);
        Assert.Equal(++version, booking.Version);

        booking.CompleteInspection(Utc(3, 9), AdminId, "normal wear");
        Assert.Equal(++version, booking.Version);

        RentalDamageDeduction deduction = booking.AddDamageDeduction("Scratch", "خدش", 5_000, "evidence/v", AdminId, Utc(3, 10));
        Assert.Equal(++version, booking.Version);

        booking.ApproveDamageDeduction(deduction.Id, AdminId, Utc(4, 8));
        Assert.Equal(++version, booking.Version);

        booking.RecordFamilyDamageDispute(deduction.Id, UserId, Utc(4, 9), "please recheck");
        Assert.Equal(++version, booking.Version);

        booking.ResolveDamageDispute(deduction.Id, AdminId, Utc(4, 10));
        Assert.Equal(++version, booking.Version);
    }

    [Fact]
    public void Approving_a_deduction_after_the_deposit_is_settled_is_refused()
    {
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        booking.RecordDelivery(Utc(2, 8), AdminId);
        booking.RecordReturn(Utc(3, 8), Utc(13, 8), AdminId);
        RentalDamageDeduction deduction = booking.AddDamageDeduction("Late claim", null, 5_000, "evidence/late", AdminId, Utc(3, 9));

        long award = booking.ComputeDepositSettlementAmount(Utc(4, 8)); // nothing approved yet — full deposit
        booking.BeginRefund(RentalRefundMovementKind.DepositSettlement, award, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(4, 8));
        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, AdminId, "PROV-S3", "evidence/settle", Utc(4, 9));
        booking.MarkDepositSettled(Utc(4, 10), AdminId);

        // Nothing is left to net after settlement: a later approval is refused.
        Assert.Throws<DomainException>(() => booking.ApproveDamageDeduction(deduction.Id, AdminId, Utc(5, 8)));
    }

    // ----- Verified-failure claim release -----

    [Fact]
    public void A_verified_failure_releases_the_claim_and_a_fresh_claim_can_start()
    {
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        long captured = booking.CapturedTotalMinorUnits;

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 12));
        booking.ReleaseRefundClaim(Utc(1, 13));

        Assert.Null(booking.PendingRefundKind);
        Assert.Null(booking.PendingRefundCaseId);
        Assert.Null(booking.PendingRefundAmountMinorUnits);

        // A fresh claim under a NEW case id starts cleanly after the release.
        Guid freshCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, freshCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 14));
        Assert.Equal(freshCaseId, booking.PendingRefundCaseId);

        booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, UserId, null, "evidence/retry", Utc(1, 15));
        Assert.Throws<DomainException>(() => booking.ReleaseRefundClaim(Utc(1, 16))); // the completion already cleared the claim
    }

    [Fact]
    public void Completing_a_claim_under_a_different_destination_than_the_claim_is_refused()
    {
        // M3 destination binding: the claim's persisted destination is
        // authoritative — a completion toward any other destination never lands
        // and the claim survives intact for a correctly-destined resume.
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        long captured = booking.CapturedTotalMinorUnits;

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, Guid.CreateVersion7(), RentalRefundDestination.AppCredit, Utc(1, 12));

        DomainException refused = Assert.Throws<DomainException>(
            () => booking.CompleteRefund(RentalRefundDestination.OriginalPaymentMethod, UserId, "PROV-1", "evidence/x", Utc(1, 13)));
        Assert.Contains("different destination", refused.Message);

        // The claim is untouched and completes only toward its own destination.
        Assert.NotNull(booking.PendingRefundKind);
        booking.CompleteRefund(RentalRefundDestination.AppCredit, UserId, null, "evidence/x", Utc(1, 14));
        Assert.Null(booking.PendingRefundKind);
        Assert.Null(booking.PendingRefundDestination);
    }

    [Fact]
    public void Releasing_a_claim_without_one_in_flight_is_refused()
    {
        RentalBooking booking = NewBooking(2, Utc(1, 9));
        Assert.Throws<DomainException>(() => booking.ReleaseRefundClaim(Utc(1, 10)));
    }
}
