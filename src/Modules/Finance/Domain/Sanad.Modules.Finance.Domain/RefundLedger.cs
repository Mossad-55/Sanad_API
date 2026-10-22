using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

/// <summary>
/// The append-only aggregate over one refund case's ledger.
/// <para>
/// Owner decision #17 ("ambiguous provider timeout outcomes are reconciled
/// before retry to prevent duplicate refunds") and #16 ("approval alone is not
/// completion") are enforced here, not in callers: the aggregate only ever
/// <em>adds</em> <see cref="RefundLedgerEntry"/> rows, transaction evidence is
/// mandatory before a transfer can be confirmed, and an unresolved timeout
/// blocks <em>every</em> transition except its explicit reconciliation.
/// </para>
/// <para>
/// Owner decision #4 ("never refund more than captured funds less previous
/// refunds") is enforced by callers against two aggregate views, never
/// mixed: <see cref="TotalCompleted"/> is the money that actually reached the
/// customer (the "prior refunds" term), so a completion check is
/// <c>award + TotalCompleted() ≤ captured − priorRefunds</c>;
/// <see cref="NetRecorded"/> is the conservative in-flight exposure (it also
/// counts approved-but-unpaid and ambiguous-timeout amounts, and failures as
/// signed reversals), used as the risk ceiling while a case is open.
/// </para>
/// </summary>
public sealed class RefundLedger : AggregateRoot<Guid>
{
    private readonly List<RefundLedgerEntry> _entries = [];

    private RefundLedger() { }

    private RefundLedger(Guid id, Guid refundCaseId) : base(id)
    {
        RefundCaseId = refundCaseId;
    }

    /// <summary>Correlation key shared by every entry of this case.</summary>
    public Guid RefundCaseId { get; private set; }

    public IReadOnlyCollection<RefundLedgerEntry> Entries => _entries.AsReadOnly();

    /// <summary>
    /// Latest entry by append order, or null for a case that has not recorded
    /// anything yet. Ordered by the persisted per-ledger <see
    /// cref="RefundLedgerEntry.Sequence"/>, because neither collection
    /// insertion order (EF does not preserve it across loads) nor version-7
    /// ids (millisecond-precision time order) is a reliable creation-order key.
    /// </summary>
    public RefundLedgerEntry? LatestEntry =>
        _entries.Count == 0 ? null : _entries.OrderBy(entry => entry.Sequence).Last();

    /// <summary>
    /// True while a timed-out provider outcome is unresolved (Owner decision
    /// #17): the latest recorded entry is still the ambiguous timeout, because
    /// the append gate below refuses every other transition until
    /// <see cref="ReconcileTimeout"/> runs.
    /// </summary>
    public bool HasUnresolvedTimeout =>
        LatestEntry?.EntryType == RefundLedgerEntryType.TimedOut;

    /// <summary>
    /// Signed net of every <em>money-movement</em> entry, in piastres:
    /// approvals, confirmed transfers, posted credit, failures and ambiguous
    /// timeouts count; the informational pre-completion entries (Requested,
    /// TransferInitiated) never move money and do not count (#35). See the
    /// type-level doc for how this relates to <see cref="TotalCompleted"/>
    /// under decision #4.
    /// </summary>
    public Money NetRecorded()
    {
        Money total = Money.Zero;
        foreach (RefundLedgerEntry entry in _entries)
        {
            if (entry.EntryType is RefundLedgerEntryType.Requested or RefundLedgerEntryType.TransferInitiated)
                continue;
            total = total.Add(entry.Amount);
        }

        return total;
    }

    /// <summary>Total actually refunded to the customer: confirmed transfers plus posted credit.</summary>
    public Money TotalCompleted()
    {
        Money total = Money.Zero;
        foreach (RefundLedgerEntry entry in _entries)
        {
            if (entry.EntryType is RefundLedgerEntryType.TransferConfirmed or RefundLedgerEntryType.CreditBalancePosted)
                total = total.Add(entry.Amount);
        }

        return total;
    }

    public static RefundLedger Open(Guid refundCaseId) =>
        refundCaseId == Guid.Empty
            ? throw new DomainException("A refund ledger requires a refund case.")
            : new RefundLedger(Guid.CreateVersion7(), refundCaseId);

    /// <summary>Customer submitted the refund request. Starts the initial-response clock (#2).</summary>
    public RefundLedgerEntry Request(
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? evidenceReference = null) =>
        Append(RefundLedgerEntryType.Requested, amount, occurredOnUtc, actorUserId, evidenceReference, null);

    /// <summary>SuperAdmin approved the award. Starts the seven-business-day processing clock (#2, #12).</summary>
    public RefundLedgerEntry Approve(
        Money amount,
        DateTime occurredOnUtc,
        Guid actorUserId,
        string? evidenceReference = null) =>
        Append(RefundLedgerEntryType.Approved, amount, occurredOnUtc, actorUserId, evidenceReference, null);

    /// <summary>The award was refused. No money moves.</summary>
    public RefundLedgerEntry Reject(
        DateTime occurredOnUtc,
        Guid actorUserId,
        string? evidenceReference = null) =>
        Append(RefundLedgerEntryType.Rejected, Money.Zero, occurredOnUtc, actorUserId, evidenceReference, null);

    /// <summary>
    /// A bank/wallet transfer was instructed. Not completion: the customer's
    /// selected destination is mandatory evidence (#16), and the transfer is
    /// still open until <see cref="ConfirmTransfer"/> sees a reference.
    /// </summary>
    public RefundLedgerEntry InitiateTransfer(
        Money amount,
        DateTime occurredOnUtc,
        Guid actorUserId,
        string? destinationEvidenceReference)
    {
        if (string.IsNullOrWhiteSpace(destinationEvidenceReference))
            throw new DomainException(
                "This refund transition requires supporting evidence or a provider reference.");

        return Append(
            RefundLedgerEntryType.TransferInitiated,
            amount,
            occurredOnUtc,
            actorUserId,
            destinationEvidenceReference,
            null);
    }

    /// <summary>
    /// Confirms completion with a provider/transfer reference and completion
    /// evidence (#16). Refused while a timeout is unresolved (#17).
    /// </summary>
    public RefundLedgerEntry ConfirmTransfer(
        Money amount,
        DateTime occurredOnUtc,
        Guid actorUserId,
        string providerReference,
        string? completionEvidenceReference)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
            throw new DomainException(
                "This refund transition requires supporting evidence or a provider reference.");

        return Append(
            RefundLedgerEntryType.TransferConfirmed,
            amount,
            occurredOnUtc,
            actorUserId,
            completionEvidenceReference,
            providerReference);
    }

    /// <summary>Records the customer's choice to take app credit instead of a transfer (#10, #15). A null actor is the documented "system acted" value.</summary>
    public RefundLedgerEntry PostCredit(
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? evidenceReference = null) =>
        Append(RefundLedgerEntryType.CreditBalancePosted, amount, occurredOnUtc, actorUserId, evidenceReference, null);

    /// <summary>
    /// Records a failed transfer attempt, with the provider's failure reference
    /// where supplied. This is the only transition whose amount may be
    /// negative: reversals carry signed deltas.
    /// </summary>
    public RefundLedgerEntry Fail(
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? providerReference = null) =>
        Append(RefundLedgerEntryType.Failed, amount, occurredOnUtc, actorUserId, null, providerReference, allowDuringUnresolvedTimeout: false);

    /// <summary>
    /// Records an ambiguous provider outcome. Never a retry and never a
    /// success: the case is now reconciled before anything else happens (#17).
    /// </summary>
    public RefundLedgerEntry Timeout(
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? providerReference = null) =>
        Append(RefundLedgerEntryType.TimedOut, amount, occurredOnUtc, actorUserId, null, providerReference);

    /// <summary>
    /// Records the reconciliation of a previous ambiguous outcome. Carries the
    /// evidence that resolved it; the reconciliation itself is not a money
    /// movement, so the amount is always zero. This is the only transition
    /// permitted while a timeout is unresolved (#17).
    /// </summary>
    public RefundLedgerEntry ReconcileTimeout(
        DateTime occurredOnUtc,
        Guid actorUserId,
        string evidenceReference,
        string? providerReference = null)
    {
        if (!HasUnresolvedTimeout)
            throw new DomainException(
                "The refund case is not in a state that allows this transition.");

        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new DomainException(
                "This refund transition requires supporting evidence or a provider reference.");

        return Add(RefundLedgerEntry.Create(
            RefundCaseId,
            RefundLedgerEntryType.Failed,
            Money.Zero,
            occurredOnUtc,
            actorUserId,
            evidenceReference,
            providerReference));
    }

    /// <summary>
    /// The single append choke point: assigns the per-ledger append position
    /// (the only reliable creation-order key) and adds the entry. Append-only
    /// is structural — there is no remove or replace path.
    /// </summary>
    private RefundLedgerEntry Add(RefundLedgerEntry entry)
    {
        entry.AssignSequence(_entries.Count == 0 ? 1 : _entries.Max(existing => existing.Sequence) + 1);
        _entries.Add(entry);
        return entry;
    }

    private RefundLedgerEntry Append(
        RefundLedgerEntryType entryType,
        Money amount,
        DateTime occurredOnUtc,
        Guid? actorUserId,
        string? evidenceReference,
        string? providerReference,
        bool allowDuringUnresolvedTimeout = false)
    {
        // Owner decision #17: while a provider outcome is ambiguous, the ONLY
        // permitted transition is its explicit reconciliation. Every retry,
        // confirmation or credit path is blocked here, not per-method.
        if (!allowDuringUnresolvedTimeout && HasUnresolvedTimeout)
            throw new DomainException(
                "A refund case with an unresolved provider timeout must be reconciled before anything else can be recorded.");

        // Award-bearing transitions never carry a sign: a zero-piastre award is
        // valid (#31), but a negative award is meaningless. Only Fail records
        // signed reversal deltas.
        if (entryType is not (RefundLedgerEntryType.Failed or RefundLedgerEntryType.Rejected) && amount.IsNegative)
            throw new DomainException(
                "A refund ledger movement cannot be negative.");

        RefundLedgerEntry entry = RefundLedgerEntry.Create(
            RefundCaseId,
            entryType,
            amount,
            occurredOnUtc,
            actorUserId,
            evidenceReference,
            providerReference);

        return Add(entry);
    }
}
