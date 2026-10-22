using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

/// <summary>
/// The cumulative refundable budget of one <em>capture</em> (Owner decision #4:
/// "never refund more than captured funds less previous refunds"; #6: the
/// shutdown award is the unused portion of <em>that</em> paid period).
/// <para>
/// Clause 9 produces several <em>different</em> refund cases against the same
/// captured money: a duplicate-charge case is derived from one payment attempt,
/// a shutdown case from the attempt that paid for the current period. Those cases
/// carry different refund-case ids and different idempotency keys, so per-key
/// uniqueness cannot stop them from refunding the same money twice, in either
/// order. This row is the single serialization point they share: every award
/// recorded through <c>ISubscriptionRefundRecorder</c> is claimed against one
/// <see cref="PaymentAttemptId"/>.
/// </para>
/// <para>
/// The budget is deliberately scoped to a CAPTURE rather than to the
/// subscription. A lifetime budget would let money already consumed in an older
/// period supply headroom for the current one — e.g. refunding a current-period
/// duplicate charge and then half of the same current period would "fit" under a
/// lifetime total even though it refunds the current period twice. Captured funds
/// are per period, so the ceiling must be too. <see cref="SubscriptionId"/> is an
/// indexed audit column that carries the correlation without widening the
/// ceiling.
/// </para>
/// <para>
/// Accounting is <em>reservation</em>-based, deliberately. An approval is the
/// money-core's in-flight exposure (<see cref="RefundLedgerEntryType.Approved"/>),
/// so it is claimed once, at the moment it is recorded. Completion
/// (<see cref="RefundLedgerEntryType.TransferConfirmed"/> /
/// <see cref="RefundLedgerEntryType.CreditBalancePosted"/>) is the <em>same</em>
/// money finally moving, so it is <b>not</b> claimed again — summing approved and
/// completed ledger entries would count one award twice.
/// </para>
/// <para>
/// <see cref="CapturedMinorUnits"/> is the amount actually paid for that specific
/// capture/period, supplied by the purchase owner on every write and never
/// caller-supplied. The cumulative <see cref="ClaimedMinorUnits"/> is owned here
/// and never supplied by a caller, so no caller-trusted prior-refund offset
/// exists.
/// </para>
/// </summary>
public sealed class SubscriptionRefundBudget : Entity<Guid>
{
    private SubscriptionRefundBudget() { }

    private SubscriptionRefundBudget(
        Guid id,
        Guid paymentAttemptId,
        Guid subscriptionId,
        Guid familyId,
        long capturedMinorUnits,
        DateTime createdOnUtc) : base(id)
    {
        PaymentAttemptId = paymentAttemptId;
        SubscriptionId = subscriptionId;
        FamilyId = familyId;
        CapturedMinorUnits = capturedMinorUnits;
        ClaimedMinorUnits = 0;
        Version = 1;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    /// <summary>
    /// The capture this budget belongs to — the succeeded payment attempt whose
    /// settled consideration it may refund. Unique: exactly one budget per capture
    /// exists, and it is the row every clause-9 case against that capture
    /// serializes on.
    /// </summary>
    public Guid PaymentAttemptId { get; private set; }

    /// <summary>
    /// Audit correlation only (indexed, not unique): every budget of a
    /// subscription can be found without letting one period's awards draw on
    /// another period's capture.
    /// </summary>
    public Guid SubscriptionId { get; private set; }

    public Guid FamilyId { get; private set; }

    /// <summary>
    /// What was actually captured for this attempt/period, in piastres. That is
    /// the whole ceiling: a duplicate charge may refund it in full, a shutdown
    /// award the unused portion of it, and the sum may never exceed it.
    /// </summary>
    public long CapturedMinorUnits { get; private set; }

    /// <summary>Cumulative reserved (approved) exposure in piastres. Never caller-supplied.</summary>
    public long ClaimedMinorUnits { get; private set; }

    /// <summary>Optimistic concurrency token: the row is the serialization point.</summary>
    public int Version { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>What may still be awarded before this capture is exhausted; never negative.</summary>
    public long RemainingMinorUnits
    {
        get
        {
            long remaining = CapturedMinorUnits - ClaimedMinorUnits;
            return remaining < 0 ? 0 : remaining;
        }
    }

    public static SubscriptionRefundBudget Open(
        Guid paymentAttemptId,
        Guid subscriptionId,
        Guid familyId,
        long capturedMinorUnits,
        DateTime createdOnUtc)
    {
        if (paymentAttemptId == Guid.Empty)
            throw new DomainException("A subscription refund budget requires the payment attempt it is scoped to.");
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A subscription refund budget requires a subscription.");
        if (familyId == Guid.Empty)
            throw new DomainException("A subscription refund budget requires a family account.");
        if (capturedMinorUnits < 0)
            throw new DomainException("A subscription refund budget cannot be negative.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A subscription refund budget timestamp must be UTC.");

        return new SubscriptionRefundBudget(
            Guid.CreateVersion7(),
            paymentAttemptId,
            subscriptionId,
            familyId,
            capturedMinorUnits,
            createdOnUtc);
    }

    /// <summary>
    /// Reserves <paramref name="awardMinorUnits"/> against this capture and
    /// returns false when the award would exceed what is still refundable. A
    /// caller that gets false must record no movement at all: silently trimming
    /// an award would under-refund a full duplicate charge and misreport the
    /// outcome, so the seam refuses instead.
    /// <para>
    /// <paramref name="capturedMinorUnits"/> re-states the capture's captured
    /// amount. Capacity only ever grows, and it is <em>justified</em> here: one
    /// capture can legitimately be read through two money bases — the attempt's
    /// incremental <c>TotalPayable</c> (duplicate charge) and the paid period's
    /// consideration (shutdown, which for a paid plan change is the target gross
    /// <c>BasePrice</c>, the incremental charge plus the credited portion). Taking
    /// the larger keeps the ceiling at the period's true consideration and stops a
    /// stale or incremental reader from shrinking a ceiling another case already
    /// relied on. It can never exceed the capture, because both bases describe the
    /// same settled payment.
    /// </para>
    /// <para>
    /// The transition is all-or-nothing: a refused claim mutates nothing, so a
    /// rejected award can never leak a partial increase into a later save.
    /// </para>
    /// </summary>
    public bool TryClaim(long awardMinorUnits, long capturedMinorUnits, DateTime occurredOnUtc)
    {
        if (awardMinorUnits < 0)
            throw new DomainException("A subscription refund award cannot be negative.");
        if (capturedMinorUnits < 0)
            throw new DomainException("A subscription refund budget cannot be negative.");
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A subscription refund budget timestamp must be UTC.");

        long capacity = capturedMinorUnits > CapturedMinorUnits ? capturedMinorUnits : CapturedMinorUnits;
        long claimed = ClaimedMinorUnits + awardMinorUnits;
        if (claimed > capacity)
            return false;

        CapturedMinorUnits = capacity;
        ClaimedMinorUnits = claimed;
        // Increment unconditionally once the claim is accepted: the token is the
        // row's serialization guarantee, not an amount.
        Version++;
        UpdatedOnUtc = occurredOnUtc;
        return true;
    }
}
