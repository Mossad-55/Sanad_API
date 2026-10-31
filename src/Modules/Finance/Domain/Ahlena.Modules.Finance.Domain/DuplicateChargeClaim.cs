using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// The decision state of one family-submitted duplicate-charge claim (clause 9).
/// <para>
/// The support workflow is deliberately the simplest audible machine: a claim is
/// Submitted, and a reviewer either Approves it or Rejects it, straight from
/// Submitted. There is no begin-review pickup phase and no notification fan-out —
/// the state exists so the decision is auditable and so execution can never begin
/// before a recorded verdict.
/// </para>
/// </summary>
public enum DuplicateChargeClaimState
{
    /// <summary>The family submitted the claim; no verdict yet.</summary>
    Submitted = 1,

    /// <summary>The reviewer rejected the claim. No money moved and none ever will under it.</summary>
    Rejected = 2,

    /// <summary>
    /// The reviewer approved the full verified capture basis; the award is
    /// reserved against the capture budget and the claim is awaiting an
    /// explicit execution trigger.
    /// </summary>
    Approved = 3
}

/// <summary>
/// The execution phase of an approved duplicate-charge claim (Owner decision #16:
/// approval alone is not completion). Mirrors the shipped booking refund
/// lifecycle: a committed in-flight attempt, an ambiguous provider outcome that
/// blocks everything but reconciliation, a failed attempt that may be retried a
/// bounded number of times, and a final confirmed completion.
/// </summary>
public enum DuplicateChargeExecutionState
{
    /// <summary>No execution has started (not approved yet, or not triggered).</summary>
    None = 1,

    /// <summary>Approved and waiting for an operator to trigger execution.</summary>
    AwaitingExecution = 2,

    /// <summary>An execution attempt is in flight; its intent was persisted before the provider call.</summary>
    Executing = 3,

    /// <summary>The provider outcome was ambiguous; reconciliation is the only way forward.</summary>
    Ambiguous = 4,

    /// <summary>The last execution attempt definitively failed; a bounded retry may re-enter.</summary>
    Failed = 5,

    /// <summary>The confirmed transfer completed; the money reached the customer.</summary>
    Completed = 6
}

/// <summary>
/// One family-submitted claim that a duplicate/erroneous subscription charge be
/// refunded in full (clause 9), with its support decision and execution audit.
/// <para>
/// The claim is a <em>Financed</em> record: submission never moves money and
/// never pays (the recorder seam owns every money movement), and approval
/// reserves the award against the capture budget through the frozen
/// <see cref="ISubscriptionRefundRecorder"/>-guarded money core in the same unit
/// of work as the verdict. The ledger correlation id is derived from the
/// corrected payment attempt, so the case is stable across submission, decision
/// and execution and shares one serialization point with any legacy award that
/// may already exist for the same capture.
/// </para>
/// <para>
/// Two reasons are stored separately, on purpose (the duplicate-charge support
/// contract): <see cref="FamilyReason"/> is the customer-facing note a reviewer
/// writes to the family, while <see cref="DecisionNote"/> and
/// <see cref="VerifiedBasis"/> are internal review Record — they are never
/// serialized on any family-facing read.
/// </para>
/// </summary>
public sealed class DuplicateChargeClaim : AggregateRoot<Guid>
{
    /// <summary>Matches the house idempotency-key width (care-home visit precedent).</summary>
    public const int MaximumIdempotencyKeyLength = 200;

    public const int MaximumEvidenceReferenceLength = 300;

    /// <summary>The mandatory customer-facing note on a rejection (writable on approval too).</summary>
    public const int MaximumFamilyReasonLength = 1000;

    /// <summary>Internal review note; never serialized to the family.</summary>
    public const int MaximumDecisionNoteLength = 2000;

    /// <summary>The reviewer's internal and mandatory statement of verified basis on an approval.</summary>
    public const int MaximumVerifiedBasisLength = 2000;

    /// <summary>The affirmative, operator-verified provider evidence reference for a NotMoved reconciliation.</summary>
    public const int MaximumNotMovedEvidenceLength = 300;

    public const int MaximumGatewayErrorCodeLength = 100;

    public const int MaximumProviderRefundIdLength = 200;

    public const int MaximumAccountTypeLength = 50;

    /// <summary>
    /// Execution retries are bounded so a persistently refusing provider cannot be
    /// hammered indefinitely; further attempts require review, never a loop.
    /// </summary>
    public const int MaximumExecutionAttempts = 5;

    private DuplicateChargeClaim() { }

    private DuplicateChargeClaim(
        Guid id,
        Guid familyAccountId,
        Guid subscriptionId,
        Guid paymentAttemptId,
        Guid refundCaseId,
        long basisMinorUnits,
        DateTime customerReportedOnUtc,
        string? evidenceReference,
        string? idempotencyKey,
        DateTime submittedOnUtc) : base(id)
    {
        FamilyAccountId = familyAccountId;
        SubscriptionId = subscriptionId;
        PaymentAttemptId = paymentAttemptId;
        RefundCaseId = refundCaseId;
        AwardMinorUnits = basisMinorUnits;
        Currency = Money.CurrencyCode;
        CustomerReportedOnUtc = customerReportedOnUtc;
        EvidenceReference = evidenceReference;
        IdempotencyKey = idempotencyKey;
        State = DuplicateChargeClaimState.Submitted;
        ExecutionState = DuplicateChargeExecutionState.None;
        ExecutionAttemptCount = 0;
        SubmittedOnUtc = submittedOnUtc;
        CreatedOnUtc = submittedOnUtc;
        UpdatedOnUtc = submittedOnUtc;
        Version = 1;
    }

    public Guid FamilyAccountId { get; private set; }

    /// <summary>Audit correlation: the subscription the refunded capture belongs to.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>
    /// The duplicated capture the claim draws on; the refund case and the shared
    /// capture budget are both keyed on this attempt.
    /// </summary>
    public Guid PaymentAttemptId { get; private set; }

    /// <summary>
    /// The ledger correlation shared with the case's <see cref="RefundLedger"/>:
    /// the deterministic per-attempt derivation also used by the legacy direct
    /// -award path, so legacy rows and claim rows serialize on one case id.
    /// </summary>
    public Guid RefundCaseId { get; private set; }

    /// <summary>
    /// The verified full capture basis in piastres, fixed at submission from the
    /// succeeded settlement's server-derived payable. Approval claims exactly
    /// this amount — it is never trimmed.
    /// </summary>
    public long AwardMinorUnits { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string? IdempotencyKey { get; private set; }

    /// <summary>The family's own evidence reference; audit only, never displayed to staff as a fact.</summary>
    public string? EvidenceReference { get; private set; }

    /// <summary>The instant the family reported the issue, validated UTC at submission; audit only.</summary>
    public DateTime CustomerReportedOnUtc { get; private set; }

    public DateTime SubmittedOnUtc { get; private set; }

    public DuplicateChargeClaimState State { get; private set; }

    public DuplicateChargeExecutionState ExecutionState { get; private set; }

    public Guid? DecidedByAccountId { get; private set; }

    public string? DecidedByAccountType { get; private set; }

    public DateTime? DecidedOnUtc { get; private set; }

    /// <summary>Customer-facing decision note written by the reviewer; the ONLY decision text a family read returns.</summary>
    public string? FamilyReason { get; private set; }

    /// <summary>Internal decision note; never serialized to the family.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Internal statement of the verified basis behind the approval; never serialized to the family.</summary>
    public string? VerifiedBasis { get; private set; }

    /// <summary>Unique per execution attempt; the fencing token executed/reconcile decisions bind to.</summary>
    public Guid? ExecutionAttemptId { get; private set; }

    public Guid? ExecutedByAccountId { get; private set; }

    public DateTime? ExecutionStartedOnUtc { get; private set; }

    /// <summary>The award the in-flight attempt was prepared for (booking-claim precedent).</summary>
    public long? ExecutionAwardMinorUnits { get; private set; }

    public string? GatewayErrorCode { get; private set; }

    /// <summary>The provider's own refund transaction reference, recorded per attempt when known.</summary>
    public string? ProviderRefundId { get; private set; }

    /// <summary>The affirmative operator-verified provider reference behind a NotMoved reconciliation.</summary>
    public string? NotMovedProviderEvidenceReference { get; private set; }

    public DateTime? CompletedOnUtc { get; private set; }

    /// <summary>How many execution attempts have been triggered (bounded).</summary>
    public int ExecutionAttemptCount { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>Optimistic concurrency token: the row is the verdict/execution serialization point.</summary>
    public int Version { get; private set; }

    public static DuplicateChargeClaim Submit(
        Guid familyAccountId,
        Guid subscriptionId,
        Guid paymentAttemptId,
        Guid refundCaseId,
        long basisMinorUnits,
        DateTime customerReportedOnUtc,
        string? evidenceReference,
        string? idempotencyKey,
        DateTime submittedOnUtc)
    {
        if (familyAccountId == Guid.Empty)
            throw new DomainException("A duplicate-charge claim requires a family account.");
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A duplicate-charge claim requires the subscription the charge belongs to.");
        if (paymentAttemptId == Guid.Empty)
            throw new DomainException("A duplicate-charge claim requires the payment attempt it corrects.");
        if (refundCaseId == Guid.Empty)
            throw new DomainException("A duplicate-charge claim requires a refund case.");
        if (basisMinorUnits <= 0)
            throw new DomainException("A duplicate-charge claim requires a positive verified capture basis.");
        if (customerReportedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A duplicate-charge claim customer report instant must be UTC.");
        if (submittedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A duplicate-charge claim submission instant must be UTC.");

        return new DuplicateChargeClaim(
            Guid.CreateVersion7(),
            familyAccountId,
            subscriptionId,
            paymentAttemptId,
            refundCaseId,
            basisMinorUnits,
            customerReportedOnUtc,
            Normalize(evidenceReference, MaximumEvidenceReferenceLength),
            Normalize(idempotencyKey, MaximumIdempotencyKeyLength),
            submittedOnUtc);
    }

    /// <summary>
    /// Records the support decision that approves the full verified basis. The
    /// award is exactly <see cref="AwardMinorUnits"/> — never trimmed; the
    /// caller refuses with a capture-basis conflict when the capture budget
    /// cannot fund the full amount.
    /// </summary>
    public void Approve(
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string verifiedBasis,
        string? decisionNote,
        string? familyReason)
    {
        ThrowUnlessDecidable();
        ThrowUnlessUtc(decidedOnUtc);

        if (decidedByAccountId == Guid.Empty)
            throw new DomainException("An approval requires its decider.");
        if (string.IsNullOrWhiteSpace(verifiedBasis))
            throw new DomainException("An approval requires a statement of verified basis.");

        State = DuplicateChargeClaimState.Approved;
        ExecutionState = DuplicateChargeExecutionState.AwaitingExecution;
        DecidedByAccountId = decidedByAccountId;
        DecidedByAccountType = Normalize(decidedByAccountType, MaximumAccountTypeLength);
        DecidedOnUtc = decidedOnUtc;
        VerifiedBasis = Normalize(verifiedBasis, MaximumVerifiedBasisLength)
            ?? throw new DomainException("An approval requires a statement of verified basis.");
        DecisionNote = Normalize(decisionNote, MaximumDecisionNoteLength);
        FamilyReason = Normalize(familyReason, MaximumFamilyReasonLength);
        Touch(decidedOnUtc);
    }

    /// <summary>
    /// Records the support decision that rejects the claim. A customer-facing
    /// reason is mandatory on rejection; no money moves and none ever will under
    /// this claim.
    /// </summary>
    public void Reject(
        Guid decidedByAccountId,
        string? decidedByAccountType,
        DateTime decidedOnUtc,
        string familyReason,
        string? decisionNote)
    {
        ThrowUnlessDecidable();
        ThrowUnlessUtc(decidedOnUtc);

        if (decidedByAccountId == Guid.Empty)
            throw new DomainException("A rejection requires its decider.");
        if (string.IsNullOrWhiteSpace(familyReason))
            throw new DomainException("A rejection requires a customer-facing reason.");

        State = DuplicateChargeClaimState.Rejected;
        DecidedByAccountId = decidedByAccountId;
        DecidedByAccountType = Normalize(decidedByAccountType, MaximumAccountTypeLength);
        DecidedOnUtc = decidedOnUtc;
        FamilyReason = Normalize(familyReason, MaximumFamilyReasonLength)
            ?? throw new DomainException("A rejection requires a customer-facing reason.");
        DecisionNote = Normalize(decisionNote, MaximumDecisionNoteLength);
        Touch(decidedOnUtc);
    }

    /// <summary>
    /// Begins one execution attempt. Callers persist the resulting intent
    /// (execution fields plus the ledger's TransferInitiated) and commit BEFORE
    /// the provider call, mirroring the booking claim-before-send precedent.
    /// </summary>
    public void BeginExecution(Guid executedByAccountId, DateTime startedOnUtc)
    {
        ThrowUnlessExecutable();
        ThrowUnlessUtc(startedOnUtc);

        if (executedByAccountId == Guid.Empty)
            throw new DomainException("An execution requires its operator.");

        if (ExecutionAttemptCount >= MaximumExecutionAttempts)
            throw new DomainException(
                $"A duplicate-charge claim may be executed at most {MaximumExecutionAttempts} times.");

        State = DuplicateChargeClaimState.Approved;
        ExecutionState = DuplicateChargeExecutionState.Executing;
        ExecutionAttemptId = Guid.CreateVersion7();
        ExecutedByAccountId = executedByAccountId;
        ExecutionStartedOnUtc = startedOnUtc;
        ExecutionAwardMinorUnits = AwardMinorUnits;
        GatewayErrorCode = null;
        ProviderRefundId = null;
        CompletedOnUtc = null;
        ExecutionAttemptCount++;
        Touch(startedOnUtc);
    }

    /// <summary>Records a confirmed provider refund under the current execution attempt.</summary>
    public void RecordExecutionSuccess(string providerRefundId, DateTime completedOnUtc)
    {
        ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState.Executing);
        ThrowUnlessUtc(completedOnUtc);

        ProviderRefundId = Normalize(providerRefundId, MaximumProviderRefundIdLength)
            ?? throw new DomainException("A confirmed refund requires the provider's refund reference.");
        ExecutionState = DuplicateChargeExecutionState.Completed;
        CompletedOnUtc = completedOnUtc;
        Touch(completedOnUtc);
    }

    /// <summary>Records a definitive provider refusal under the current attempt; a bounded retry may follow.</summary>
    public void RecordExecutionRefusal(string gatewayErrorCode, DateTime occurredOnUtc)
    {
        ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState.Executing);
        ThrowUnlessUtc(occurredOnUtc);

        GatewayErrorCode = Normalize(gatewayErrorCode, MaximumGatewayErrorCodeLength);
        ExecutionState = DuplicateChargeExecutionState.Failed;
        Touch(occurredOnUtc);
    }

    /// <summary>
    /// Records an unconfirmable provider outcome under the current attempt. The
    /// case is blocked except for reconciliation, exactly like the frozen
    /// RefundLedger's unresolved-timeout gate.
    /// </summary>
    public void RecordExecutionAmbiguous(string? gatewayErrorCode, DateTime occurredOnUtc)
    {
        ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState.Executing);
        ThrowUnlessUtc(occurredOnUtc);

        GatewayErrorCode = Normalize(gatewayErrorCode, MaximumGatewayErrorCodeLength);
        ExecutionState = DuplicateChargeExecutionState.Ambiguous;
        Touch(occurredOnUtc);
    }

    /// <summary>
    /// Reconciles the in-flight attempt of Executing state as definitively MOVED.
    /// A NotMoved reconciliation of an in-flight attempt is deliberately absent
    /// from the aggregate: a claim that may still be moving money at the provider
    /// can never be cleared on an operator's assertion (the shipped booking
    /// rule, decision #17).
    /// </summary>
    public void ReconcileInFlightMoved(string providerRefundId, DateTime reconciledOnUtc)
    {
        ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState.Executing);
        ThrowUnlessUtc(reconciledOnUtc);

        ProviderRefundId = Normalize(providerRefundId, MaximumProviderRefundIdLength)
            ?? throw new DomainException("A reconciliation that records money as refunded must include the provider's refund reference.");
        ExecutionState = DuplicateChargeExecutionState.Completed;
        CompletedOnUtc = reconciledOnUtc;
        Touch(reconciledOnUtc);
    }

    /// <summary>
    /// Reconciles an Ambiguous outcome. A Moved reconciliation closes the claim
    /// against the provider's own refund reference; a NotMoved reconciliation
    /// requires the affirmative operator-verified provider evidence reference
    /// (beyond mere point-in-time absence of movement) and leaves the award
    /// failed so a bounded retry can re-enter after review.
    /// </summary>
    public void ReconcileAmbiguousOutcome(
        bool refundDidMove,
        string? providerRefundId,
        string? notMovedProviderEvidenceReference,
        DateTime reconciledOnUtc)
    {
        ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState.Ambiguous);
        ThrowUnlessUtc(reconciledOnUtc);

        if (refundDidMove)
        {
            ProviderRefundId = Normalize(providerRefundId, MaximumProviderRefundIdLength)
                ?? throw new DomainException("A reconciliation that records money as refunded must include the provider's refund reference.");
            ExecutionState = DuplicateChargeExecutionState.Completed;
            CompletedOnUtc = reconciledOnUtc;
        }
        else
        {
            NotMovedProviderEvidenceReference =
                Normalize(notMovedProviderEvidenceReference, MaximumNotMovedEvidenceLength)
                ?? throw new DomainException(
                    "A reconciliation that records money as NOT moved requires affirmative, operator-verified provider evidence, not just the absence of movement.");
            ExecutionState = DuplicateChargeExecutionState.Failed;
        }

        Touch(reconciledOnUtc);
    }

    /// <summary>True when this claim already carries the award reservation (approved or past approval).</summary>
    public bool IsAwardedOrBeyond =>
        State == DuplicateChargeClaimState.Approved;

    private void ThrowUnlessDecidable()
    {
        if (State != DuplicateChargeClaimState.Submitted)
            throw new DomainException("A claim that already carries a verdict cannot be decided again.");
        if (ExecutionState != DuplicateChargeExecutionState.None)
            throw new DomainException("A claim with an execution phase cannot be decided again.");
    }

    private void ThrowUnlessExecutable()
    {
        if (State != DuplicateChargeClaimState.Approved)
            throw new DomainException("Only an approved claim can be executed.");
        if (ExecutionState
                is not (DuplicateChargeExecutionState.AwaitingExecution
                    or DuplicateChargeExecutionState.Failed))
            throw new DomainException(
                "Only an un-triggered or previously failed claim can begin a new execution attempt.");
    }

    private void ThrowUnlessCurrentAttemptIs(DuplicateChargeExecutionState expected)
    {
        if (ExecutionState != expected || ExecutionAttemptId is null || ExecutionAwardMinorUnits is null)
            throw new DomainException("The claim has no execution attempt in the state this transition requires.");
    }

    private static void ThrowUnlessUtc(DateTime instant)
    {
        if (instant.Kind != DateTimeKind.Utc)
            throw new DomainException("A duplicate-charge claim transition instant must be UTC.");
    }

    private void Touch(DateTime instant)
    {
        Version++;
        UpdatedOnUtc = instant;
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new DomainException($"A duplicate-charge claim text must not exceed {maximumLength} characters.");

        return trimmed;
    }
}
