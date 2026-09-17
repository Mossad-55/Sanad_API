using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// <see cref="BookingCancellationPolicyInput.ResolveCaptureEvidence"/> and
/// <see cref="BookingCancellationPolicyInput.FromBooking"/>: the adapters that read capture history,
/// an acceptance time and a visit start off a persisted booking. Every source aggregate here is built
/// through existing public <see cref="Booking"/> factories and transitions — no test-only production
/// constructor is introduced and no persistence layer is involved.
/// </summary>
public sealed class BookingCaptureEvidenceAdapterTests
{
    // -----------------------------------------------------------------------------------------
    // Genuinely unpaid: nothing captured.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FreshUnpaidBooking_ResolvesNotCaptured()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();

        BookingCaptureEvidence evidence = BookingCancellationPolicyInput.ResolveCaptureEvidence(booking);

        Assert.Equal(BookingCaptureEvidence.NotCaptured, evidence);
        Assert.Empty(booking.PaymentTransactions);
        Assert.Null(booking.PaidOnUtc);
        Assert.Null(booking.PaymobTransactionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnpaidBookingWithOnlyUnsettledAttempts_ResolvesNotCaptured(bool failed)
    {
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, BookingCancellationBookingFactory.PaymentOnUtc);

        if (failed)
        {
            booking.RecordPaymentFailure(
                "PM-ORDER-1",
                "PM-TXN-DECLINED",
                BookingCancellationBookingFactory.PaymentOnUtc.AddMinutes(1));
        }

        Assert.Equal(BookingCaptureEvidence.NotCaptured, BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));

        // A captured-but-unsettled booking is still "nothing captured": the policy may answer for it.
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                BookingCancellationBookingFactory.PaymentOnUtc.AddMinutes(2)));

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, decision.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
    }

    // -----------------------------------------------------------------------------------------
    // A succeeded transaction is capture evidence on its own.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void SucceededTransaction_ResolvesCaptured()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePaidBooking();

        Assert.Equal(BookingCaptureEvidence.Captured, BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));
        Assert.Equal(PaymentTransactionStatus.Succeeded, booking.PaymentTransactions.Single().Status);
    }

    [Fact]
    public void SucceededTransactionWithoutProviderReference_IsStillCapturedAndNeverReportsNoRefundDue()
    {
        // The provider reference is missing but the settlement is proven by the succeeded
        // transaction: that gap is an application concern, never a reason to say "no refund due".
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, BookingCancellationBookingFactory.PaymentOnUtc);
        booking.MarkAsPaid("PM-ORDER-1", string.Empty, BookingCancellationBookingFactory.PaymentOnUtc);

        Assert.True(string.IsNullOrEmpty(booking.PaymobTransactionId));
        Assert.Equal(PaymentTransactionStatus.Succeeded, booking.PaymentTransactions.Single().Status);
        Assert.Equal(BookingCaptureEvidence.Captured, BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                BookingCancellationBookingFactory.PaymentOnUtc.AddMinutes(30)));

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.NotEqual(BookingRefundEntitlement.NoRefundDue, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance,
            decision.RefundDecisionReason);
        Assert.NotEqual(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
    }

    // -----------------------------------------------------------------------------------------
    // Legacy markers: incomplete history is uncertainty, never proof that nothing was paid.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void PaidMarkerWithoutAnyTransaction_ResolvesAmbiguous_NotUnpaid()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();
        booking.MarkAsPaid(
            "PM-ORDER-LEGACY",
            "PM-TXN-LEGACY",
            BookingCancellationBookingFactory.PaymentOnUtc);

        Assert.Empty(booking.PaymentTransactions);
        Assert.NotNull(booking.PaidOnUtc);

        BookingCaptureEvidence evidence = BookingCancellationPolicyInput.ResolveCaptureEvidence(booking);

        Assert.Equal(BookingCaptureEvidence.Ambiguous, evidence);
        Assert.NotEqual(BookingCaptureEvidence.NotCaptured, evidence);
    }

    [Fact]
    public void PaidMarkerWhoseOnlyTransactionBelongsToAnotherOrder_ResolvesAmbiguous()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-A", PaymentMethod.Wallet, BookingCancellationBookingFactory.PaymentOnUtc);
        booking.MarkAsPaid("PM-ORDER-B", "PM-TXN-B", BookingCancellationBookingFactory.PaymentOnUtc);

        Assert.Equal(PaymentTransactionStatus.Pending, booking.PaymentTransactions.Single().Status);
        Assert.Equal(BookingCaptureEvidence.Ambiguous, BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));
    }

    [Fact]
    public void AmbiguousEvidence_FailsClosedInsteadOfBecomingACancellationOutcome()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();
        booking.MarkAsPaid("PM-ORDER-LEGACY", "PM-TXN-LEGACY", BookingCancellationBookingFactory.PaymentOnUtc);

        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.FromBooking(
            booking,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            BookingCancellationBookingFactory.PaymentOnUtc.AddMinutes(5));

        Assert.Equal(BookingCaptureEvidence.Ambiguous, input.CaptureEvidence);
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    // -----------------------------------------------------------------------------------------
    // FromBooking: faithful mapping, no mutation.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FromBooking_PreservesStatusAcceptanceAndSuppliedArgumentsAndMutatesNothing()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePaidAndAcceptedBooking();

        int transactionCount = booking.PaymentTransactions.Count;
        DateTime updatedOnUtc = booking.UpdatedOnUtc;
        DateTime confirmedOnUtc = booking.ConfirmedOnUtc!.Value;
        DateTime cancelledOnUtc = confirmedOnUtc.AddMinutes(30);
        BookingCancellationFeedback? feedback = BookingCancellationFeedback.CreateOptionalNote("  running late  ");

        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.FromBooking(
            booking,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            cancelledOnUtc,
            feedback);

        Assert.Equal(BookingStatus.Confirmed, input.Status);
        Assert.Equal(BookingCancellationActorSide.Family, input.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, input.Action);
        Assert.Equal(booking.ConfirmedOnUtc, input.ConfirmedOnUtc);
        Assert.Equal(booking.StartedOnUtc, input.StartedOnUtc);
        Assert.Null(input.StartedOnUtc);
        Assert.Equal(cancelledOnUtc, input.CancelledOnUtc);
        Assert.Equal(BookingCaptureEvidence.Captured, input.CaptureEvidence);
        Assert.Same(feedback, input.Feedback);

        // Reading a booking for a decision never changes it.
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(confirmedOnUtc, booking.ConfirmedOnUtc);
        Assert.Equal(transactionCount, booking.PaymentTransactions.Count);
        Assert.Equal(updatedOnUtc, booking.UpdatedOnUtc);
        Assert.NotNull(booking.PaidOnUtc);

        // The mapped input is exactly what the policy needs: 30 minutes of grace remains, so the
        // captured amount is refundable in full.
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow,
            decision.RefundDecisionReason);
        Assert.Equal(booking.ConfirmedOnUtc, decision.ConfirmedOnUtcUsed);
    }

    [Fact]
    public void FromBooking_CarriesARecordedVisitStartSoThePolicyCanRefuseTheCancellation()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePaidAndAcceptedBooking();
        booking.StartVisit(booking.ConfirmedOnUtc!.Value.AddMinutes(60));

        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.FromBooking(
            booking,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            booking.ConfirmedOnUtc.Value.AddMinutes(61));

        Assert.Equal(BookingStatus.InProgress, input.Status);
        Assert.Equal(booking.StartedOnUtc, input.StartedOnUtc);
        Assert.NotNull(input.StartedOnUtc);
        Assert.Equal(booking.ConfirmedOnUtc, input.ConfirmedOnUtc);

        // The adapter maps the started visit faithfully instead of hiding it, and the policy refuses.
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    [Fact]
    public void FromBooking_WithoutFeedbackOnAnAcceptedBookingStillMapsAndLetsThePolicyRejectIt()
    {
        Booking booking = BookingCancellationBookingFactory.CreatePaidAndAcceptedBooking();

        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.FromBooking(
            booking,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            booking.ConfirmedOnUtc!.Value.AddMinutes(10));

        Assert.Null(input.Feedback);
        DomainException error = Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));

        Assert.Contains("requires a reason category", error.Message);
    }

    [Fact]
    public void BothAdapters_RejectAMissingBooking()
    {
        Assert.Throws<ArgumentNullException>(() => BookingCancellationPolicyInput.ResolveCaptureEvidence(null!));

        Assert.Throws<ArgumentNullException>(() => BookingCancellationPolicyInput.FromBooking(
            null!,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            BookingCancellationBookingFactory.PaymentOnUtc));
    }

    // -----------------------------------------------------------------------------------------
    // Representational limit of the current public API, recorded instead of widened around:
    // a Confirmed booking can only be reached by paying first, and paying either settles a matched
    // transaction (Captured) or leaves legacy markers (Ambiguous). There is therefore NO booking
    // reachable through the public factories that is Confirmed yet resolves to NotCaptured, and no
    // booking whose succeeded transaction is later downgraded to NotCaptured. The policy-level
    // rejection of "paid state + NotCaptured/Ambiguous evidence" is exercised on the direct input
    // record (see BookingCancellationPolicyTests), which is the seam the contract exposes for it.
    // Likewise MarkRefunded settles a transaction to Refunded, which the adapter reports as
    // Ambiguous rather than NotCaptured; a refund already recorded on an ended booking is refused by
    // the status rule before evidence is ever consulted.
    // -----------------------------------------------------------------------------------------
}

/// <summary>
/// Builds real <see cref="Booking"/> aggregates through the existing public factories and state
/// transitions only, on fixed UTC timestamps, so adapter tests never need a test-only production
/// constructor, a database or a payment provider.
/// </summary>
internal static class BookingCancellationBookingFactory
{
    internal static readonly DateTime CreatedOnUtc = new(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime PaymentOnUtc = new(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime AcceptanceOnUtc = new(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime AcceptanceDeadlineUtc = new(2026, 3, 6, 10, 0, 0, DateTimeKind.Utc);

    internal static Booking CreatePendingPaymentBooking() =>
        Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(CreatedOnUtc).AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            AcceptanceDeadlineUtc,
            DateOnly.FromDateTime(CreatedOnUtc),
            CreatedOnUtc);

    internal static Booking CreatePaidBooking()
    {
        Booking booking = CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, PaymentOnUtc);
        booking.MarkAsPaid("PM-ORDER-1", "PM-TXN-1", PaymentOnUtc);
        return booking;
    }

    internal static Booking CreatePaidAndAcceptedBooking()
    {
        Booking booking = CreatePaidBooking();
        booking.AcceptByCaregiver(AcceptanceOnUtc);
        return booking;
    }
}
