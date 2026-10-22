using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// <see cref="BookingRefundSettlementExecutor"/>: the two destinations an awarded refund can take
/// (Owner decision #15). The executor itself never writes money — it calls the published Finance
/// credit seam or the payment-provider seam and reports what was actually settled — and a
/// zero-piastre award settles nothing without being an error (decision #31).
/// <para>
/// NOT RUN. Authored in the W3-F1 slice; the single build/test gate belongs to Mastermind.
/// </para>
/// </summary>
public sealed class BookingRefundSettlementExecutorTests
{
    private static readonly DateTime ScheduledStartUtc =
        new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CreatedOnUtc =
        new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ZeroAward_SettlesNothingThroughEitherDestination()
    {
        Booking booking = PaidBooking();

        BookingRefundSettlement provider = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 0, new RecordingPaymobClient(), CreatedOnUtc, CancellationToken.None);

        BookingRefundSettlement credit = await BookingRefundSettlementExecutor.SettleWithAppCreditAsync(
            booking, awardedMinorUnits: 0, Guid.NewGuid(), new RecordingCreditWriter(), CreatedOnUtc, CancellationToken.None);

        Assert.Equal(BookingRefundDestination.None, provider.Destination);
        Assert.Equal(BookingRefundDestination.None, credit.Destination);
        Assert.Equal(0, booking.RefundedMinorUnits);
    }

    [Fact]
    public async Task ProviderSettlement_SendsTheAwardNotTheCapturedAmount_AndAdvancesTheCeiling()
    {
        Booking booking = EndedPaidBooking();
        var paymob = new RecordingPaymobClient();

        // A 50% award on a 400.00 captured booking: the gateway must be asked for 200.00, never
        // the whole 400.00, or the customer is over-refunded (BLK-1).
        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 20000, paymob, CreatedOnUtc, CancellationToken.None);

        Assert.Equal(BookingRefundDestination.Provider, settlement.Destination);
        Assert.True(settlement.IsCompleted);
        Assert.Equal(20000, settlement.SettledMinorUnits);

        RefundCall call = Assert.Single(paymob.RefundCalls);
        Assert.Equal(200.00m, call.Amount);

        // MAJ-4: a successful provider settlement advances the #4 ceiling exactly like the credit
        // path, so a replay cannot refund the same money twice.
        Assert.Equal(20000, booking.RefundedMinorUnits);
        Assert.Equal(20000, booking.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task ProviderSettlement_AnUnconfirmedOutcomeMarksTheBookingAndBlocksEveryRetry()
    {
        Booking booking = PaidBooking();
        var paymob = new RecordingPaymobClient { RefundSucceeds = false, RefundOutcomeUnknown = true };

        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 20000, paymob, CreatedOnUtc, CancellationToken.None);

        Assert.False(settlement.IsCompleted);
        Assert.True(booking.IsRefundOutcomeAmbiguous);
        Assert.Equal(20000, booking.RefundAmbiguityAwardMinorUnits);

        // MAJ-5 / decision #17: nothing was booked (the movement is unconfirmed, not completed) and
        // every further attempt is refused until an operator reconciles it.
        Assert.Equal(0, booking.RefundedMinorUnits);
        Assert.Equal(40000, booking.RefundableCeilingMinorUnits());
        await Assert.ThrowsAsync<DomainException>(() =>
            BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking, awardedMinorUnits: 20000, paymob, CreatedOnUtc, CancellationToken.None));
    }

    [Fact]
    public async Task ProviderSettlement_AGatewayRejectionIsRetryableAndRaisesNoMarker()
    {
        Booking booking = EndedPaidBooking();
        var paymob = new RecordingPaymobClient
        {
            RefundSucceeds = false,
            RefundErrorCode = "Paymob.RefundRejected"
        };

        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 20000, paymob, CreatedOnUtc, CancellationToken.None);

        Assert.False(settlement.IsCompleted);
        Assert.False(booking.IsRefundOutcomeAmbiguous);
        Assert.Equal(40000, booking.RefundableCeilingMinorUnits());

        // A refusal moved nothing, so the award is still due and may be retried.
        RecordingPaymobClient succeeding = paymob.WithSuccess();
        BookingRefundSettlement retried = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 20000, succeeding, CreatedOnUtc, CancellationToken.None);

        Assert.True(retried.IsCompleted);
    }

    [Fact]
    public async Task ProviderSettlement_ReportsTheGatewayErrorWithoutAdvancingTheCeiling()
    {
        Booking booking = PaidBooking();
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };

        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
            booking, awardedMinorUnits: 40000, paymob, CreatedOnUtc, CancellationToken.None);

        Assert.False(settlement.IsCompleted);
        Assert.NotNull(settlement.Error);
        Assert.Equal(40000, booking.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task ProviderSettlement_ForAnAwardAboveTheCeiling_IsRefusedByTheLedgerGuard()
    {
        Booking booking = PaidBooking();
        booking.RecordRefundedAmount(35000, CreatedOnUtc);
        var paymob = new RecordingPaymobClient();

        // Defence in depth (minor 15): even a caller that bypasses the policy cannot push
        // cumulative refunds past the captured amount.
        await Assert.ThrowsAsync<DomainException>(() =>
            BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking, awardedMinorUnits: 20000, paymob, CreatedOnUtc, CancellationToken.None));
    }

    [Fact]
    public async Task AppCreditSettlement_PostsTheAwardAndAdvancesTheCeiling()
    {
        Booking booking = PaidBooking();
        var writer = new RecordingCreditWriter();

        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithAppCreditAsync(
            booking, awardedMinorUnits: 25000, Guid.NewGuid(), writer, CreatedOnUtc, CancellationToken.None);

        Assert.Equal(BookingRefundDestination.AppCredit, settlement.Destination);
        Assert.True(settlement.IsCompleted);
        Assert.Equal(25000, Assert.Single(writer.Posted).Amount.MinorUnits);
        Assert.Equal(15000, booking.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task AppCreditSettlement_FailureMovesNoMoney()
    {
        Booking booking = PaidBooking();
        var writer = new RecordingCreditWriter { Succeeds = false };

        BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithAppCreditAsync(
            booking, awardedMinorUnits: 25000, Guid.NewGuid(), writer, CreatedOnUtc, CancellationToken.None);

        Assert.False(settlement.IsCompleted);
        Assert.Equal(0, booking.RefundedMinorUnits);
        Assert.Equal(40000, booking.RefundableCeilingMinorUnits());
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static Booking PaidBooking()
    {
        Booking booking = Booking.Create(
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
            createdOnUtc: CreatedOnUtc);

        // MAJ-7: capture evidence resolves only from a succeeded transaction, so the intent must be
        // recorded before the payment is marked paid — otherwise the policy sees Ambiguous capture
        // evidence and (correctly) refuses to decide.
        booking.RecordPaymentIntent("order", PaymentMethod.Card, CreatedOnUtc);
        booking.MarkAsPaid("order", "txn", CreatedOnUtc);
        return booking;
    }

    private static Booking EndedPaidBooking()
    {
        Booking booking = PaidBooking();

        // In production the booking is always cancelled/ended before a provider refund
        // settles; MarkRefunded refuses to fire on a still-live booking.
        booking.CancelByFamily("family cancelled", CreatedOnUtc);
        return booking;
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private readonly List<RefundCall> _refundCalls = [];

        public IReadOnlyList<RefundCall> RefundCalls => _refundCalls;

        public bool RefundSucceeds { get; init; } = true;

        /// <summary>
        /// When true, a failed refund reports "the outcome could not be confirmed" rather than a
        /// clean refusal — the case that must raise the decision #17 marker.
        /// </summary>
        public bool RefundOutcomeUnknown { get; init; }

        public string RefundErrorCode { get; init; } = "Paymob.RefundFailed";

        public RecordingPaymobClient WithSuccess() => new() { RefundSucceeds = true };

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.BookingId.Value.ToString(),
                    "test-intention",
                    "test-client-secret",
                    "test-public-key")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            _refundCalls.Add(new RefundCall(paymobTransactionId, amount));

            if (RefundSucceeds)
                return Task.FromResult(Result<string?>.Success("test-refund-transaction"));

            string code = RefundOutcomeUnknown ? "Paymob.RefundOutcomeUnknown" : RefundErrorCode;

            return Task.FromResult(Result<string?>.Failure(
                new Error(code, "The gateway did not confirm the refund.")));
        }
    }

    private sealed record PostedCredit(Guid FamilyId, Money Amount, Guid ActorUserId, string? EvidenceReference);

    private sealed class RecordingCreditWriter : IFamilyCreditBalanceWriter
    {
        private readonly List<PostedCredit> _posted = [];

        public IReadOnlyList<PostedCredit> Posted => _posted;

        public bool Succeeds { get; init; } = true;

        public Task<Result<FamilyCreditBalanceSnapshot>> PostAsync(
            Guid familyId,
            Money amount,
            Guid actorUserId,
            string? evidenceReference,
            CancellationToken cancellationToken)
        {
            if (!Succeeds)
            {
                return Task.FromResult(Result<FamilyCreditBalanceSnapshot>.Failure(
                    FamilyCreditBalanceErrors.Unknown));
            }

            _posted.Add(new PostedCredit(familyId, amount, actorUserId, evidenceReference));

            return Task.FromResult(Result<FamilyCreditBalanceSnapshot>.Success(
                new FamilyCreditBalanceSnapshot(
                    familyId,
                    amount.MinorUnits,
                    Money.CurrencyCode,
                    CreatedOnUtc)));
        }
    }
}
