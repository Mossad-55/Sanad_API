using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// The material a subscription refund request was made with, so a replay of the
/// same idempotency key can be told apart from a conflicting one.
/// <para>
/// This is not a money record and not a receipts table: it holds no award, no
/// balance and no outcome. It exists so the column-level unique
/// <c>(family_id, idempotency_key)</c> constraint has a row to arbitrate on,
/// which is the house M7 mechanism (design pin item 4). The outcome itself is
/// read back from the append-only <see cref="RefundLedger"/> the request
/// produced.
/// </para>
/// </summary>
public sealed class SubscriptionRefundRequest : Entity<Guid>
{
    /// <summary>
    /// Matches the house idempotency-key width established by the care-home
    /// visit create (<c>CareHomeVisit.MaximumIdempotencyKeyLength</c>).
    /// </summary>
    public const int MaximumIdempotencyKeyLength = 200;

    public const int MaximumEvidenceReferenceLength = 300;

    private SubscriptionRefundRequest() { }

    private SubscriptionRefundRequest(
        Guid id,
        Guid refundCaseId,
        Guid familyId,
        Guid subscriptionId,
        string idempotencyKey,
        long requestedAmountMinorUnits,
        SubscriptionRefundReason reason,
        string? evidenceReference,
        DateTime createdOnUtc) : base(id)
    {
        RefundCaseId = refundCaseId;
        FamilyId = familyId;
        SubscriptionId = subscriptionId;
        IdempotencyKey = idempotencyKey;
        RequestedAmountMinorUnits = requestedAmountMinorUnits;
        Reason = reason;
        EvidenceReference = evidenceReference;
        CreatedOnUtc = createdOnUtc;
    }

    /// <summary>The refund case this request owns; the ledger is correlated by the same value.</summary>
    public Guid RefundCaseId { get; private set; }

    public Guid FamilyId { get; private set; }

    /// <summary>
    /// The subscription the refunded capture belongs to, kept for audit
    /// correlation. The refund ceiling itself is the capture
    /// (<see cref="SubscriptionRefundBudget.PaymentAttemptId"/>), not this value:
    /// it is stored so the record remains readable and attributable without
    /// re-deriving the budget link.
    /// </summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>
    /// The caller's key. Unique per family by index, so two families may reuse
    /// the same textual key without colliding — the same scoping the
    /// partner-contact and care-home visit precedents use.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>The requested award in piastres, for the same-content comparison on replay.</summary>
    public long RequestedAmountMinorUnits { get; private set; }

    public SubscriptionRefundReason Reason { get; private set; }

    public string? EvidenceReference { get; private set; }

    /// <summary>
    /// The instant the award was first recorded at. It is <em>not</em> part of the
    /// replay comparison: <see cref="MatchesContent"/> deliberately ignores the
    /// instant, because a replay legitimately arrives at a later wall-clock time
    /// and must still return the original outcome rather than a conflict.
    /// </summary>
    public DateTime CreatedOnUtc { get; private set; }

    public static SubscriptionRefundRequest Create(
        Guid refundCaseId,
        Guid familyId,
        Guid subscriptionId,
        string idempotencyKey,
        long requestedAmountMinorUnits,
        SubscriptionRefundReason reason,
        string? evidenceReference,
        DateTime createdOnUtc)
    {
        if (refundCaseId == Guid.Empty)
            throw new DomainException("A subscription refund requires a refund case.");
        if (familyId == Guid.Empty)
            throw new DomainException("A subscription refund requires a family account.");
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A subscription refund requires the subscription it is claimed against.");
        if (!Enum.IsDefined(reason))
            throw new DomainException("A subscription refund requires a known reason.");

        string key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > MaximumIdempotencyKeyLength)
            throw new DomainException(
                $"Idempotency key must be between 1 and {MaximumIdempotencyKeyLength} characters.");
        if (requestedAmountMinorUnits < 0)
            throw new DomainException("A subscription refund award cannot be negative.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A subscription refund timestamp must be UTC.");

        return new SubscriptionRefundRequest(
            Guid.CreateVersion7(),
            refundCaseId,
            familyId,
            subscriptionId,
            key,
            requestedAmountMinorUnits,
            reason,
            Normalize(evidenceReference),
            createdOnUtc);
    }

    /// <summary>
    /// True when a stored request carries the same content as an incoming
    /// replay. A key match with any difference is a conflict, never a second
    /// award.
    /// </summary>
    public bool MatchesContent(Guid refundCaseId, long amountMinorUnits, SubscriptionRefundReason reason, string? evidenceReference) =>
        RefundCaseId == refundCaseId &&
        RequestedAmountMinorUnits == amountMinorUnits &&
        Reason == reason &&
        string.Equals(EvidenceReference, Normalize(evidenceReference), StringComparison.Ordinal);

    private static string? Normalize(string? evidenceReference) =>
        string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
}
