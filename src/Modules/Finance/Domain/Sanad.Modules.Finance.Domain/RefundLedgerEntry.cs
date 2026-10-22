using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

/// <summary>
/// The lifecycle states a refund case can record. Owner decisions #12, #16 and
/// #17 require the investigation/approval split (SupportAdmin investigates,
/// SuperAdmin approves), transfer evidence before completion and explicit
/// reconciliation of ambiguous provider outcomes. Each transition appends an
/// entry; nothing is ever rewritten.
/// </summary>
public enum RefundLedgerEntryType
{
    /// <summary>Customer submitted a refund request. Starts the two-business-day initial-response clock (#2).</summary>
    Requested = 1,

    /// <summary>SuperAdmin approved the reviewed award. Starts the seven-business-day processing clock (#2, #12).</summary>
    Approved = 2,

    /// <summary>The refund was refused. Terminal unless a new request is opened.</summary>
    Rejected = 3,

    /// <summary>A bank/wallet transfer was instructed but is not yet confirmed (#16).</summary>
    TransferInitiated = 4,

    /// <summary>Completion confirmed with destination and transfer reference evidence (#16).</summary>
    TransferConfirmed = 5,

    /// <summary>The provider/transfer attempt failed. Retry is allowed after reconciliation.</summary>
    Failed = 6,

    /// <summary>
    /// Outcome unknown (provider timeout). Reconciled before retry to prevent a
    /// duplicate refund (#17); the case must not be retried blindly.
    /// </summary>
    TimedOut = 7,

    /// <summary>Customer chose app credit instead of the original payment method (#10, #15).</summary>
    CreditBalancePosted = 8
}

/// <summary>
/// One append-only entry in a refund case's ledger.
/// <para>
/// Append-only is structural, not conventional: the type exposes no public
/// mutator and there is no update/delete path, so a stored entry cannot be
/// rewritten. Entries are only ever produced by the aggregate's factory
/// methods and added to the database — mirroring
/// <see cref="PlatformControlAuditEntry"/>.
/// </para>
/// </summary>
public sealed class RefundLedgerEntry : Entity<Guid>
{
    public const int MaximumEvidenceReferenceLength = 300;
    public const int MaximumProviderReferenceLength = 200;
    public const int MaximumCaseReferenceLength = 100;

    private RefundLedgerEntry() { }

    private RefundLedgerEntry(
        Guid id,
        Guid refundCaseId,
        RefundLedgerEntryType entryType,
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? evidenceReference,
        string? providerReference) : base(id)
    {
        RefundCaseId = refundCaseId;
        EntryType = entryType;
        Amount = amount;
        OccurredOnUtc = occurredOnUtc;
        ActorUserId = actorUserId;
        EvidenceReference = evidenceReference;
        ProviderReference = providerReference;
    }

    /// <summary>Correlation key for the refund request/case this entry belongs to.</summary>
    public Guid RefundCaseId { get; private set; }

    /// <summary>
    /// The entry's append position within its ledger. This is the ONLY
    /// reliable creation-order key: version-7 ids are time-ordered at
    /// millisecond precision only, and two entries appended in the same
    /// millisecond order by their random bits.
    /// </summary>
    public long Sequence { get; private set; }

    /// <summary>Assigns the append position. Called by the aggregate only.</summary>
    internal void AssignSequence(long sequence) => Sequence = sequence;

    public RefundLedgerEntryType EntryType { get; private set; }

    /// <summary>Signed amount in piastres. Zero is valid (a zero-piastre award, #31).</summary>
    public Money Amount { get; private set; } = Money.Zero;

    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Null means the platform itself acted (for example a timeout sweep).</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>Reference to stored supporting evidence (damage report, transfer receipt, customer choice).</summary>
    public string? EvidenceReference { get; private set; }

    /// <summary>Gateway/transfer reference. Supplied by the provider, never invented here.</summary>
    public string? ProviderReference { get; private set; }

    internal static RefundLedgerEntry Create(
        Guid refundCaseId,
        RefundLedgerEntryType entryType,
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? evidenceReference,
        string? providerReference)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (refundCaseId == Guid.Empty)
            throw new DomainException("A refund ledger entry requires a refund case.");
        if (!Enum.IsDefined(entryType))
            throw new DomainException("A refund ledger entry requires a known entry type.");
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A refund ledger timestamp must be UTC.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("An attributable actor must be a real user id, or null for a system entry.");

        return new(
            Guid.CreateVersion7(),
            refundCaseId,
            entryType,
            amount,
            occurredOnUtc,
            actorUserId,
            Normalize(evidenceReference, MaximumEvidenceReferenceLength, nameof(evidenceReference)),
            Normalize(providerReference, MaximumProviderReferenceLength, nameof(providerReference)));
    }

    private static string? Normalize(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"Refund ledger {field} must not exceed {maximumLength} characters.");

        return normalized;
    }
}
