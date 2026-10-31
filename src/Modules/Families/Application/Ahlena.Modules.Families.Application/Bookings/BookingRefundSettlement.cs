using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Families.Application.Bookings;

/// <summary>
/// Persists the outcome of a refund attempt that was already claimed and committed before its
/// provider call. The save can lose an optimistic-concurrency race — most importantly against an
/// operator reconciliation that ran while the provider call was still in flight. A real provider
/// settlement must never be discarded just because a competing writer won the row: when the
/// durable row already records the award, the late confirmation is an idempotent success.
/// <para>
/// When the durable row does not record the award, the outcome is not invented: the claim that was
/// committed before the send stays in flight, so the attempt remains blocked until an operator
/// reconciles it with verified provider evidence (decision #17).
/// </para>
/// </summary>
public static class BookingRefundPersistence
{
    public static async Task<bool> TrySaveClaimedOutcomeAsync(
        IFamiliesDbContext dbContext,
        BookingId bookingId,
        long settledMinorUnits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (settledMinorUnits <= 0)
            {
                return false;
            }

            long refundedMinorUnits = await dbContext.Bookings
                .AsNoTracking()
                .Where(booking => booking.Id == bookingId)
                .Select(booking => booking.RefundedMinorUnits)
                .SingleOrDefaultAsync(cancellationToken);

            // A single booking carries at most one cancellation fact and therefore one award; a
            // settled amount that already covers the award means this exact movement is recorded,
            // not that some other money was refunded.
            return refundedMinorUnits >= settledMinorUnits;
        }
    }
}

/// <summary>
/// How much a retry of an incomplete refund may send. Extracted from the admin handler so the rule
/// — and its fail-closed branch — can be asserted directly instead of only through a handler.
/// </summary>
public static class BookingRefundRetryAmount
{
    /// <summary>
    /// Resolves the piastre amount a retry may send.
    /// <para>
    /// A revision-2 fact records the exact award the policy decided, so the retry sends that award
    /// less what has already settled, capped by the remaining captured budget — never the whole
    /// captured amount, and never a second payment for an award that already settled. A
    /// revision-1 fact is amount-free by design (the whole captured amount, or nothing), so its
    /// retry keeps the legacy semantics of the remaining ceiling. A revision-2 fact with no recorded
    /// award is incomplete evidence and fails closed.
    /// </para>
    /// Returns 0 with <paramref name="error"/> set when the retry must not proceed.
    /// </summary>
    public static long Resolve(
        int? factPolicyVersion,
        long? factAwardedMinorUnits,
        long capturedCeiling,
        long alreadyRefundedMinorUnits,
        out Error? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capturedCeiling);

        error = null;

        if (factAwardedMinorUnits is { } awarded)
        {
            long stillOwed = awarded - alreadyRefundedMinorUnits;

            if (stillOwed <= 0)
            {
                error = new Error(
                    "Bookings.AlreadyRefunded",
                    "The policy award for this cancellation is already settled.");
                return 0;
            }

            return Math.Min(stillOwed, capturedCeiling);
        }

        if (factPolicyVersion is { } version
            && version >= BookingRefundPolicyVersion.ScheduledStartVersion)
        {
            error = new Error(
                "Bookings.RefundNotEligible",
                "The revision-2 cancellation fact does not record the award, so the retry cannot establish what is owed.");
            return 0;
        }

        return capturedCeiling;
    }
}

/// <summary>
/// How a refund entitlement is translated into money movement. Kept beside the handlers rather
/// than inside a handler so the same mapping can be asserted directly, and so the two destinations
/// the Owner contract names (decision #15) stay distinguishable at the type level.
/// </summary>
public enum BookingRefundDestination
{
    /// <summary>No money moves: the policy awarded nothing or there was nothing captured.</summary>
    None = 1,

    /// <summary>Money goes back through the payment provider.</summary>
    Provider = 2,

    /// <summary>
    /// The customer elected app credit (Owner decision #15). It is a real, non-withdrawable
    /// balance posted through the Finance module's credit seam — not a refund label — and it is
    /// recorded on the refund ledger as a credit-balance movement.
    /// </summary>
    AppCredit = 3
}

/// <summary>
/// The outcome of attempting to settle one award. <see cref="SettledMinorUnits"/> records what was
/// actually moved, which is what the booking's refund ceiling advances by; a zero-piastre award
/// settles nothing and is not an error (decision #31).
/// </summary>
public sealed record BookingRefundSettlement(
    BookingRefundDestination Destination,
    long SettledMinorUnits,
    bool IsCompleted,
    Error? Error = null)
{
    public static BookingRefundSettlement Nothing { get; } =
        new(BookingRefundDestination.None, 0, IsCompleted: false);
}

/// <summary>Failure vocabulary for the settlement seams, so every path maps to the same codes.</summary>
public static class BookingRefundSettlementErrors
{
    public static readonly Error SettlementIncomplete =
        new(
            "Bookings.RefundSettlementIncomplete",
            "The awarded refund could not be completed through its destination; the award is recorded and the movement is left outstanding for an ops retry.");

    public static readonly Error AwardExceedsCeiling =
        new(
            "Bookings.RefundExceedsCeiling",
            "The awarded refund would exceed the money available to refund for this booking.");

    /// <summary>
    /// The provider could not confirm whether the refund moved. Decision #17: this is never
    /// retried blindly — the booking is marked and an operator reconciles it first.
    /// </summary>
    public static readonly Error AmbiguousOutcome =
        new(
            "Bookings.RefundOutcomeAmbiguous",
            "The payment provider could not confirm the refund outcome; an administrator must reconcile the attempt before any further refund is made.");
}

/// <summary>
/// Moves an awarded refund to the destination the customer chose. Finance exclusively owns money
/// persistence, so this type never writes money itself: it only calls the published
/// <see cref="IFamilyCreditBalanceWriter"/> seam and the payment-provider seam, then reports what
/// was actually settled.
/// <para>
/// The <em>award</em> is what is sent to the provider, not the booking's whole captured amount:
/// <c>IPaymobClient.RefundPaymentAsync</c> takes an explicit amount and Paymob refunds that amount
/// (<c>amount_cents</c>), so a 50% clause-2 award or a package's undelivered-session award moves
/// exactly that slice. Settling the full captured amount for a partial award would over-refund the
/// customer and break the decision #4 ceiling.
/// </para>
/// <para>
/// A settlement result is only <see cref="IsCompleted"/> when the provider confirmed the movement;
/// <see cref="SettledMinorUnits"/> then reports the amount actually moved. An unconfirmed outcome
/// (<c>Paymob.RefundOutcomeUnknown</c>) is reported as such and the caller raises the booking's
/// ambiguity marker rather than retrying (decision #17).
/// </para>
/// </summary>
public static class BookingRefundSettlementExecutor
{
    /// <summary>
    /// Settles an awarded provider refund. Nothing is attempted for a zero award or an unpaid
    /// booking, so the caller can always invoke this without a pre-check.
    /// </summary>
    public static async Task<BookingRefundSettlement> SettleWithProviderAsync(
        Booking booking,
        long awardedMinorUnits,
        IPaymobClient paymobClient,
        DateTime utcNow,
        CancellationToken cancellationToken,
        Guid? claimId = null)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(paymobClient);

        if (awardedMinorUnits <= 0)
            return BookingRefundSettlement.Nothing;

        // The caller must hold the claim it committed before reaching here. A null claim keeps the
        // legacy pre-claim guard for callers that have not acquired one; the production entry
        // points all acquire one and present it, so the persisted claim is what authorises the
        // external call.
        if (claimId is null)
        {
            booking.EnsureRefundAttemptAllowed();
        }
        else if (!booking.IsRefundAttemptInFlight || booking.RefundAttemptClaimId != claimId)
        {
            throw new DomainException(
                "This caller does not hold the booking's in-flight refund claim, so it may not move money.");
        }
        else if (booking.RefundAttemptAwardMinorUnits is { } claimedAward
            && claimedAward != awardedMinorUnits)
        {
            // The claim records the exact award that was committed before the call; settling a
            // different amount would move money the durable record does not authorise.
            throw new DomainException(
                "The settled award does not match the award recorded on the refund claim.");
        }

        PaymentTransaction? paidTransaction = booking.PaymentTransactions.FirstOrDefault(
            transaction => transaction.Status == PaymentTransactionStatus.Succeeded
                && transaction.PaymobTransactionId is not null);

        if (paidTransaction is null)
            return BookingRefundSettlement.Nothing;

        // The award is the amount requested from the gateway, in major units, rounded the same way
        // every other piastre-to-major conversion in the codebase is.
        decimal requestedAmount = ToMajorUnits(awardedMinorUnits);

        Result<string?> refund = await paymobClient.RefundPaymentAsync(
            paidTransaction.PaymobTransactionId!,
            requestedAmount,
            cancellationToken);

        if (!refund.IsSuccess)
        {
            BookingRefundSettlement failed = new(
                BookingRefundDestination.Provider,
                SettledMinorUnits: 0,
                IsCompleted: false,
                refund.Error);

            // A gateway that could not confirm the outcome may still have moved the money, so the
            // booking is marked and no further attempt is allowed until an operator reconciles it.
            // The marker folds the in-flight claim into itself.
            if (IsOutcomeUnknown(refund.Error))
            {
                booking.MarkRefundOutcomeAmbiguous(
                    paidTransaction.PaymobTransactionId,
                    refund.Error!.Code,
                    utcNow,
                    awardedMinorUnits);
            }
            else
            {
                // A definitive refusal proves no money moved, so the claim is released and a
                // bounded retry stays possible; the ceiling is untouched because nothing settled.
                booking.ReleaseRefundAttempt(utcNow);
            }

            return failed;
        }

        booking.MarkRefunded(refund.Value, utcNow);

        // The settled award advances the decision #4 ceiling exactly like the app-credit path, so
        // a replay cannot refund the same money twice.
        booking.RecordRefundedAmount(awardedMinorUnits, utcNow);

        // The claim is satisfied: the movement is recorded, so the attempt is no longer in flight.
        booking.ReleaseRefundAttempt(utcNow);

        return new BookingRefundSettlement(
            BookingRefundDestination.Provider,
            awardedMinorUnits,
            IsCompleted: true);
    }

    /// <summary>
    /// Settles an awarded amount into the family's app credit (Owner decision #15). A zero award
    /// posts nothing — the credit aggregate itself refuses a non-positive posting — so a
    /// zero-piastre proration award is still recordable on the ledger while moving no balance
    /// (decision #31).
    /// </summary>
    public static async Task<BookingRefundSettlement> SettleWithAppCreditAsync(
        Booking booking,
        long awardedMinorUnits,
        Guid actingUserId,
        IFamilyCreditBalanceWriter creditWriter,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(creditWriter);

        if (awardedMinorUnits <= 0)
            return BookingRefundSettlement.Nothing;

        // Decision #17 applies to every destination: while an unconfirmed provider attempt stands,
        // posting credit would add a second movement for money that may already have gone back.
        booking.EnsureRefundAttemptAllowed();

        Result<FamilyCreditBalanceSnapshot> posted = await creditWriter.PostAsync(
            booking.FamilyId.Value,
            Money.FromMinorUnits(awardedMinorUnits),
            actingUserId,
            evidenceReference: $"booking:{booking.Id.Value}",
            cancellationToken);

        if (!posted.IsSuccess)
        {
            return new BookingRefundSettlement(
                BookingRefundDestination.AppCredit,
                SettledMinorUnits: 0,
                IsCompleted: false,
                posted.Error);
        }

        booking.RecordRefundedAmount(awardedMinorUnits, utcNow);

        return new BookingRefundSettlement(
            BookingRefundDestination.AppCredit,
            awardedMinorUnits,
            IsCompleted: true);
    }

    /// <summary>
    /// True when the provider error means "the call did not confirm what happened" rather than
    /// "the refund was refused". Only the refusal is safe to retry; the unknown is not.
    /// </summary>
    private static bool IsOutcomeUnknown(Error? error) =>
        error is not null
        && string.Equals(error.Code, "Paymob.RefundOutcomeUnknown", StringComparison.Ordinal);

    /// <summary>
    /// True when the booking has a captured provider transaction a refund would be sent against.
    /// A settlement entry point uses this to decide whether it must acquire a durably committed
    /// refund claim before calling the provider: an award with no provider capture moves no money,
    /// so it is recorded without a claim and without an external call.
    /// </summary>
    public static bool HasProviderCapture(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return booking.PaymentTransactions.Any(transaction =>
            transaction.Status == PaymentTransactionStatus.Succeeded
            && transaction.PaymobTransactionId is not null);
    }

    /// <summary>
    /// Converts piastres to the major units the provider expects, rounding half to even — the same
    /// convention <c>BookingRefundPolicyInput.ToMinorUnits</c> uses in the opposite direction.
    /// </summary>
    internal static decimal ToMajorUnits(long minorUnits) =>
        decimal.Round(minorUnits / 100m, 2, MidpointRounding.ToEven);
}
