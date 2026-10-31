using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Domain.Bookings;

public enum CareHomeBookingStatus
{
    PendingPayment = 1,
    PaidAwaitingDecision = 2,
    Accepted = 3,
    Rejected = 4,
    Expired = 5,
    Cancelled = 6,
    RefundPending = 7,
    RefundInitiated = 8,
    Refunded = 9
}

public enum CareHomeBookingPaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    RefundInitiated = 4,
    Refunded = 5
}

public enum CareHomeRefundStatus
{
    None = 0,
    Pending = 1,
    Initiated = 2,
    Failed = 3,
    Completed = 4,
    ManuallyCompleted = 5
}

public enum CareHomeCheckInDisputeStatus { Open = 1, Resolved = 2 }

public sealed class CareHomeBooking : AggregateRoot<Guid>
{
    private CareHomeBooking() { }

    private CareHomeBooking(
        Guid id,
        CareHomeId facilityId,
        UserId familyUserId,
        FamilyId familyId,
        ElderlyId elderlyId,
        Guid roomTypeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal baseAmount,
        decimal platformFeeAmount,
        decimal taxAmount,
        int chargeRuleVersion,
        string arabicName,
        string englishName,
        int age,
        string? medicalSnapshotJson,
        string contactName,
        string? contactPhone,
        string? contactRelationship,
        string? careNotes,
        DateTime utcNow,
        TimeSpan checkoutHoldDuration,
        CareHomePolicySnapshot? policySnapshot)
        : base(id)
    {
        FacilityId = facilityId;
        FamilyUserId = familyUserId;
        FamilyId = familyId;
        ElderlyId = elderlyId;
        RoomTypeId = roomTypeId;
        StartDate = startDate;
        EndDate = endDate;
        BaseAmount = baseAmount;
        PlatformFeeAmount = platformFeeAmount;
        TaxAmount = taxAmount;
        TotalAmount = baseAmount + platformFeeAmount + taxAmount;
        ChargeRuleVersion = chargeRuleVersion;
        ElderlyArabicName = arabicName;
        ElderlyEnglishName = englishName;
        ElderlyAge = age;
        MedicalSnapshotJson = medicalSnapshotJson;
        ResponsibleContactName = contactName;
        ResponsibleContactPhone = contactPhone;
        ResponsibleContactRelationship = contactRelationship;
        CareNeedsNotes = careNotes;
        Status = CareHomeBookingStatus.PendingPayment;
        PaymentStatus = CareHomeBookingPaymentStatus.Pending;
        CheckoutHoldUntilUtc = utcNow.Add(checkoutHoldDuration);
        EarliestArrivalUtc = utcNow.AddHours(24);
        Version = 1;
        CreatedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
        if (policySnapshot is not null)
        {
            RefundPolicyVersion = policySnapshot.PolicyVersion;
            RefundPolicyDocumentEffectiveOn = policySnapshot.DocumentEffectiveOn;
            CancellationTermsSnapshotJson = policySnapshot.CancellationTermsJson;
        }
    }

    public CareHomeId FacilityId { get; private set; }
    public UserId FamilyUserId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public CareHomeBookingStatus Status { get; private set; }
    public CareHomeBookingPaymentStatus PaymentStatus { get; private set; }
    public decimal BaseAmount { get; private set; }
    public decimal PlatformFeeAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public int ChargeRuleVersion { get; private set; }
    public string ElderlyArabicName { get; private set; } = string.Empty;
    public string ElderlyEnglishName { get; private set; } = string.Empty;
    public int ElderlyAge { get; private set; }
    public string? MedicalSnapshotJson { get; private set; }
    public string ResponsibleContactName { get; private set; } = string.Empty;
    public string? ResponsibleContactPhone { get; private set; }
    public string? ResponsibleContactRelationship { get; private set; }
    public string? CareNeedsNotes { get; private set; }
    public string MerchantReference { get; private set; } = string.Empty;
    public long? PaymobTransactionId { get; private set; }
    public DateTime? PaymentCompletedOnUtc { get; private set; }
    public string? RefundReference { get; private set; }
    public DateTime? RefundClaimedOnUtc { get; private set; }

    /// <summary>
    /// The stable identity of the in-flight refund attempt: generated when the
    /// claim is persisted — before the provider call — and presented back by an
    /// operator's reconciliation request, so reconciliation compares and swaps
    /// against the very attempt the operator verified. A stale operator, or a
    /// request aimed at an attempt that has since concluded and been replaced
    /// by a newer one, can never match the current claim. The id lives and dies
    /// with the claim: folded into the ambiguity marker with it when the
    /// outcome could not be confirmed, cleared whenever the attempt concludes
    /// (confirmed initiation, definitive failure, verified reconciliation).
    /// </summary>
    public Guid? RefundAttemptClaimId { get; private set; }

    public CareHomeRefundStatus RefundStatus { get; private set; }
    public decimal? RefundAmount { get; private set; }
    public string? RefundFailureReason { get; private set; }
    public DateTime? RefundCompletedOnUtc { get; private set; }
    public UserId? RefundCompletedBy { get; private set; }
    public int RefundPolicyVersion { get; private set; } = CareHomeRefundPolicyVersion.LegacyVersion;
    public DateOnly RefundPolicyDocumentEffectiveOn { get; private set; }
    public string? CancellationTermsSnapshotJson { get; private set; }

    /// <summary>
    /// True while a refund attempt on this booking ended in an outcome the
    /// gateway could not confirm (<c>Paymob.RefundOutcomeUnknown</c>) — the
    /// money may or may not have moved. Decision #17 is answered here rather
    /// than by a retry: while this marker stands no further attempt is
    /// claimable, so a blind retry can never double-refund a customer whose
    /// first refund silently succeeded. Only an operator's evidence-based
    /// reconciliation clears it. <b>This boolean is the authoritative
    /// open-question gate</b>: consumers key on it, never on the ambiguity
    /// audit fields (see <see cref="MarkRefundOutcomeAmbiguous"/> — a second
    /// ambiguous attempt overwrites the previous attempt's record).
    /// </summary>
    public bool IsRefundOutcomeAmbiguous { get; private set; }

    /// <summary>
    /// True while a provider attempt is outstanding without an outcome: a
    /// claim (and its identity) is persisted and no ambiguity marker has
    /// folded it in. This is the state an operator must never treat as
    /// abandoned — elapsed age is not proof the provider call finished — and
    /// it is exactly the state the reconcile surface refuses
    /// (<c>AttemptInconclusive</c>). A computed view of persisted state; not
    /// itself mapped to storage.
    /// </summary>
    public bool IsRefundAttemptInFlight =>
        RefundClaimedOnUtc is not null && !IsRefundOutcomeAmbiguous;

    /// <summary>
    /// True when the refund-send route may claim-and-send this booking right
    /// now: a definitive failed refund (the bounded retry), or a pending refund
    /// with no claim — the crash window between the begin-refund save and the
    /// claim save, where no send was possible because a claim always precedes
    /// a send (so an unclaimed pending row is proven not-sent). A claimed
    /// attempt — live, crashed, or folded into the ambiguity marker — and any
    /// concluded state are never armed; the send acquires its claim atomically
    /// (<see cref="TryClaimRefund"/>) and races of competing claims lose on
    /// the version token. A computed view of persisted state; not itself
    /// mapped to storage.
    /// </summary>
    public bool IsRefundSendArmed =>
        Status == CareHomeBookingStatus.RefundPending
        && RefundClaimedOnUtc is null
        && !IsRefundOutcomeAmbiguous
        && (RefundStatus == CareHomeRefundStatus.Failed
            || RefundStatus == CareHomeRefundStatus.Pending);

    /// <summary>Why the unconfirmed attempt stood, in the provider's own error code.</summary>
    public string? RefundAmbiguityReason { get; private set; }

    /// <summary>When the unconfirmed attempt was made, in UTC.</summary>
    public DateTime? RefundAmbiguityRaisedOnUtc { get; private set; }

    /// <summary>
    /// The award the unconfirmed attempt was trying to move — the refund
    /// amount already recorded when the lifecycle was entered, kept here so a
    /// verified-success reconciliation books exactly that award, once.
    /// </summary>
    public decimal? RefundAmbiguityAwardAmount { get; private set; }

    /// <summary>When an operator cleared the marker, in UTC. Null while it stands.</summary>
    public DateTime? RefundAmbiguityClearedOnUtc { get; private set; }

    /// <summary>
    /// The operator who cleared the marker with the evidence they recorded,
    /// kept as the durable record of who unblocked the booking.
    /// </summary>
    public string? RefundAmbiguityClearedBy { get; private set; }
    public DateTime? PaymentIntentClaimedOnUtc { get; private set; }
    public int? PaymentIntentMethod { get; private set; }
    public string? PaymobOrderId { get; private set; }
    public string? IntentionOrderId { get; private set; }
    public string? PaymentClientSecret { get; private set; }
    public string? PaymentPublicKey { get; private set; }
    public DateTime CheckoutHoldUntilUtc { get; private set; }
    public DateTime? DecisionHoldUntilUtc { get; private set; }
    public DateTime? DecidedOnUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime EarliestArrivalUtc { get; private set; }
    public Guid? AssignedRoomId { get; private set; }
    public Guid? AssignedBedId { get; private set; }
    public Guid? ExtensionOfBookingId { get; private set; }
    public Guid? ExtensionRootBookingId { get; private set; }
    public DateTime? ActualCheckInOnUtc { get; private set; }
    public DateTime? ActualCheckOutOnUtc { get; private set; }
    public UserId? ActualCheckInRecordedBy { get; private set; }
    public UserId? ActualCheckOutRecordedBy { get; private set; }
    public DateTime? FamilyCheckInConfirmedOnUtc { get; private set; }
    public UserId? FamilyCheckInConfirmedBy { get; private set; }

    public static CareHomeBooking Create(
        CareHomeId facilityId, UserId familyUserId, FamilyId familyId, ElderlyId elderlyId,
        Guid roomTypeId, DateOnly startDate, decimal baseAmount, decimal feeAmount,
        decimal taxAmount, int ruleVersion, string arabicName, string englishName, int age,
        string? medicalJson, string contactName, string? contactPhone, string? relationship,
        string? careNotes, DateTime utcNow,
        TimeSpan? checkoutHoldDuration = null,
        CareHomePolicySnapshot? policySnapshot = null)
    {
        TimeSpan checkoutHold = checkoutHoldDuration ?? TimeSpan.FromMinutes(15);
        if (checkoutHold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(checkoutHoldDuration));
        TimeZoneInfo cairo = GetCairoTimeZone();
        DateOnly earliestCairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow.AddHours(24), cairo));
        if (startDate < earliestCairoDate)
            throw new ArgumentException("Care-home arrival must be at least 24 hours from checkout.");
        return new CareHomeBooking(Guid.CreateVersion7(), facilityId, familyUserId, familyId, elderlyId,
            roomTypeId, startDate, startDate.AddMonths(1), baseAmount, feeAmount, taxAmount,
            ruleVersion, arabicName, englishName, age, medicalJson, contactName, contactPhone,
            relationship, careNotes, utcNow, checkoutHold, policySnapshot)
        { MerchantReference = $"chb_{Guid.CreateVersion7():N}" };
    }

    public static CareHomeBooking CreateExtension(
        CareHomeBooking currentSegment,
        UserId familyUserId,
        decimal baseAmount,
        decimal feeAmount,
        decimal taxAmount,
        int ruleVersion,
        DateTime utcNow,
        TimeSpan? checkoutHoldDuration = null,
        CareHomePolicySnapshot? policySnapshot = null)
    {
        if (currentSegment.Status != CareHomeBookingStatus.Accepted
            || currentSegment.PaymentStatus != CareHomeBookingPaymentStatus.Paid
            || currentSegment.ActualCheckOutOnUtc is not null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidExtensionState");

        TimeSpan checkoutHold = checkoutHoldDuration ?? TimeSpan.FromMinutes(15);
        if (checkoutHold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(checkoutHoldDuration));
        var extension = new CareHomeBooking(Guid.CreateVersion7(), currentSegment.FacilityId, familyUserId,
            currentSegment.FamilyId, currentSegment.ElderlyId, currentSegment.RoomTypeId,
            currentSegment.EndDate, currentSegment.EndDate.AddMonths(1), baseAmount, feeAmount, taxAmount, ruleVersion,
            currentSegment.ElderlyArabicName, currentSegment.ElderlyEnglishName, currentSegment.ElderlyAge,
            currentSegment.MedicalSnapshotJson, currentSegment.ResponsibleContactName,
            currentSegment.ResponsibleContactPhone, currentSegment.ResponsibleContactRelationship,
            currentSegment.CareNeedsNotes, utcNow, checkoutHold, policySnapshot)
        {
            MerchantReference = $"chb_{Guid.CreateVersion7():N}",
            ExtensionOfBookingId = currentSegment.Id,
            ExtensionRootBookingId = currentSegment.ExtensionRootBookingId ?? currentSegment.Id
        };
        return extension;
    }

    public bool IsCapacityActive(DateTime utcNow) =>
        Status is CareHomeBookingStatus.PendingPayment or CareHomeBookingStatus.PaidAwaitingDecision or CareHomeBookingStatus.Accepted
        && (Status != CareHomeBookingStatus.PendingPayment || CheckoutHoldUntilUtc > utcNow)
        && (Status != CareHomeBookingStatus.PaidAwaitingDecision || DecisionHoldUntilUtc > utcNow);

    public void MarkPaid(long transactionId, DateTime utcNow, TimeSpan? decisionHoldDuration = null)
    {
        if (PaymentStatus == CareHomeBookingPaymentStatus.Paid && PaymobTransactionId == transactionId) return;
        if (PaymentStatus == CareHomeBookingPaymentStatus.Paid && PaymobTransactionId != transactionId)
            throw new InvalidOperationException("CareHomes.Bookings.PaymentConflict");
        PaymentCompletedOnUtc ??= utcNow;
        if (Status is not CareHomeBookingStatus.PendingPayment || CheckoutHoldUntilUtc <= utcNow)
        {
            PaymentStatus = CareHomeBookingPaymentStatus.Paid;
            PaymobTransactionId = transactionId;
            BeginRefund("Payment succeeded after the checkout hold expired.", TotalAmount, utcNow);
            return;
        }
        PaymentStatus = CareHomeBookingPaymentStatus.Paid;
        PaymobTransactionId = transactionId;
        Status = CareHomeBookingStatus.PaidAwaitingDecision;
        TimeSpan decisionHold = decisionHoldDuration ?? TimeSpan.FromHours(24);
        if (decisionHold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(decisionHoldDuration));
        DecisionHoldUntilUtc = utcNow.Add(decisionHold);
        Touch(utcNow);
    }

    public void MarkPaymentFailed(DateTime utcNow)
    {
        if (PaymentStatus == CareHomeBookingPaymentStatus.Paid) return;
        PaymentStatus = CareHomeBookingPaymentStatus.Failed;
        Status = CareHomeBookingStatus.Cancelled;
        Touch(utcNow);
    }

    public void Accept(DateTime utcNow)
    {
        ExpireIfNeeded(utcNow);
        if (Status != CareHomeBookingStatus.PaidAwaitingDecision) throw new InvalidOperationException("Booking is not awaiting a facility decision.");
        Status = CareHomeBookingStatus.Accepted; DecidedOnUtc = utcNow; Touch(utcNow);
    }

    public void Reject(string reason, DateTime utcNow)
    {
        ExpireIfNeeded(utcNow);
        if (Status != CareHomeBookingStatus.PaidAwaitingDecision) throw new InvalidOperationException("Booking is not awaiting a facility decision.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejection reason is required.");
        BeginRefund(reason, TotalAmount, utcNow);
    }

    public decimal CancelByFamily(DateTime utcNow)
    {
        if (Status == CareHomeBookingStatus.PendingPayment && PaymentStatus == CareHomeBookingPaymentStatus.Pending)
        {
            Status = CareHomeBookingStatus.Cancelled;
            Touch(utcNow);
            return 0m;
        }

        if (Status is not (CareHomeBookingStatus.PaidAwaitingDecision or CareHomeBookingStatus.Accepted)
            || ActualCheckOutOnUtc is not null
            || PaymentStatus != CareHomeBookingPaymentStatus.Paid)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");

        // Revision dispatch (decision #7): bookings sold before the new policy
        // was activated keep their original rule exactly — full refund before
        // check-in, half the total after it, with the original AwayFromZero
        // rounding. Revision-2 bookings decide under the care-home refund
        // policy (facility terms snapshot, clause 2 default fallback).
        //
        // Revision-2 delivered-service guard (clauses 2 and 3): a recorded
        // check-in means the care was delivered, so a family cancellation
        // refunds nothing — the window schedules govern only an undelivered
        // stay; delivered service is not refundable, no matter where the
        // cancellation act sits on the 24-hour clock. Revision 1 keeps its
        // original half-after-check-in rule byte-for-byte.
        decimal amount = IsLegacyPolicy
            ? (ActualCheckInOnUtc is null
                ? TotalAmount
                : decimal.Round(TotalAmount / 2m, 2, MidpointRounding.AwayFromZero))
            : ActualCheckInOnUtc is not null
                ? 0m
                : DecideScheduledStartAward(utcNow);

        if (amount <= 0m)
        {
            // A zero award is a legitimate cancellation outcome under revision
            // 2 (clause 2/6: at or after the scheduled start — or a facility
            // tier that grants nothing — means nothing is refunded). The paid
            // booking is cancelled with its refund state left untouched rather
            // than pushed through the refund lifecycle, so projections can
            // always distinguish "cancelled, nothing refundable" from a
            // settled or in-flight refund. The facility retains the money.
            CancelPaidWithoutRefund("Cancelled by Family.", utcNow);
            return 0m;
        }

        BeginRefund("Cancelled by Family.", amount, utcNow);
        return amount;
    }

    public decimal CancelByFacility(string reason, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.Accepted || ActualCheckOutOnUtc is not null
            || PaymentStatus != CareHomeBookingPaymentStatus.Paid)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A cancellation reason is required.");
        BeginRefund(reason, TotalAmount, utcNow);
        return TotalAmount;
    }

    public void CancelUnusedExtension(DateOnly stayEndedOn, DateTime utcNow)
        => CancelUnusedExtension(stayEndedOn, utcNow, providerEnded: false);

    /// <summary>
    /// Cancels a paid but not-yet-started extension whose root stay has ended
    /// early. The actor decides the award under revision 2: a facility-ended
    /// stay is a provider failure and refunds the extension in full, while a
    /// family-ended stay applies the extension's own snapshotted schedule
    /// against the extension's scheduled start. Revision-1 extensions keep
    /// their original full-amount behavior.
    /// </summary>
    public void CancelUnusedExtension(DateOnly stayEndedOn, DateTime utcNow, bool providerEnded)
    {
        if (ExtensionRootBookingId is null || StartDate <= stayEndedOn || ActualCheckInOnUtc is not null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidExtensionState");
        if (Status is not (CareHomeBookingStatus.PendingPayment or CareHomeBookingStatus.PaidAwaitingDecision or CareHomeBookingStatus.Accepted))
            return;

        if (PaymentStatus == CareHomeBookingPaymentStatus.Paid)
        {
            decimal amount = IsLegacyPolicy || providerEnded
                ? TotalAmount
                : DecideScheduledStartAward(utcNow);
            string reason = "The existing stay ended before this extension period began.";
            if (amount <= 0m)
            {
                CancelPaidWithoutRefund(reason, utcNow);
                return;
            }
            BeginRefund(reason, amount, utcNow);
        }
        else
        {
            Status = CareHomeBookingStatus.Cancelled;
            Touch(utcNow);
        }
    }

    private void BeginRefund(string reason, decimal amount, DateTime utcNow)
    {
        if (amount <= 0m || amount > TotalAmount) throw new ArgumentOutOfRangeException(nameof(amount));
        Status = CareHomeBookingStatus.RefundPending;
        PaymentStatus = CareHomeBookingPaymentStatus.Paid;
        RefundStatus = CareHomeRefundStatus.Pending;
        RefundAmount = amount;
        RefundFailureReason = null;
        DecisionReason = reason.Trim();
        DecidedOnUtc ??= utcNow;
        Touch(utcNow);
    }

    /// <summary>True when the booking decides under its original (pre-policy) amounts.</summary>
    private bool IsLegacyPolicy => RefundPolicyVersion == CareHomeRefundPolicyVersion.LegacyVersion;

    /// <summary>
    /// The revision-2 amount for a family act against this booking's own
    /// snapshotted schedule: the facility's structured cancellation terms when
    /// the snapshot carries them, the clause 2 default schedule otherwise.
    /// Rehydrated policy state that cannot be re-read fails loudly instead of
    /// silently degrading to any schedule.
    /// </summary>
    private decimal DecideScheduledStartAward(DateTime utcNow)
    {
        CareHomeRefundPolicyVersion.FromPersisted(RefundPolicyVersion, RefundPolicyDocumentEffectiveOn);
        CareHomeCancellationTerms? terms = CareHomeCancellationTerms.Parse(CancellationTermsSnapshotJson);
        CareHomeRefundDecision decision = CareHomeRefundPolicy.Decide(
            new CareHomeRefundPolicyInput(StartDate, utcNow, TotalAmount, terms));
        return decision.AwardAmount;
    }

    /// <summary>
    /// Records a cancellation of a PAID booking that owes no refund: the
    /// booking leaves the paid money with the facility and never enters the
    /// refund lifecycle. Refund status/amount stay exactly as they were so a
    /// zero-award cancellation remains distinguishable from a settled refund
    /// in every projection.
    /// </summary>
    private void CancelPaidWithoutRefund(string reason, DateTime utcNow)
    {
        Status = CareHomeBookingStatus.Cancelled;
        PaymentStatus = CareHomeBookingPaymentStatus.Paid;
        DecisionReason = reason.Trim();
        DecidedOnUtc ??= utcNow;
        Touch(utcNow);
    }

    public void ExpireIfNeeded(DateTime utcNow)
    {
        if (Status == CareHomeBookingStatus.PendingPayment && CheckoutHoldUntilUtc <= utcNow) { Status = CareHomeBookingStatus.Expired; Touch(utcNow); }
        else if (Status == CareHomeBookingStatus.PaidAwaitingDecision && DecisionHoldUntilUtc <= utcNow) { BeginRefund("Facility decision window expired.", TotalAmount, utcNow); }
    }

    public bool TryClaimRefund(DateTime utcNow)
    {
        // An unconfirmed earlier attempt is not retryable: the provider may
        // already have moved the money (decision #17), so the claim gate
        // refuses until an operator reconciles the ambiguity. The persisted
        // claim itself (RefundClaimedOnUtc, with its stable attempt identity
        // in RefundAttemptClaimId) is the concurrency fence: it was committed
        // before the provider call, so a competing request racing the same
        // refund loses its own commit and never reaches the provider.
        if (Status != CareHomeBookingStatus.RefundPending
            || IsRefundOutcomeAmbiguous
            || RefundStatus is not (CareHomeRefundStatus.Pending or CareHomeRefundStatus.Failed)
            || RefundAmount is null
            || RefundClaimedOnUtc is not null) return false;
        RefundFailureReason = null;
        RefundClaimedOnUtc = utcNow;
        RefundAttemptClaimId = Guid.NewGuid();
        Touch(utcNow); return true;
    }

    public void MarkRefundFailed(string reason, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.RefundPending || RefundClaimedOnUtc is null
            || IsRefundOutcomeAmbiguous)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        RefundStatus = CareHomeRefundStatus.Failed;
        RefundFailureReason = string.IsNullOrWhiteSpace(reason) ? "Paymob refund failed." : reason.Trim();
        RefundClaimedOnUtc = null;
        RefundAttemptClaimId = null;
        Touch(utcNow);
    }

    /// <summary>
    /// Raises the ambiguity marker after a provider call whose outcome could
    /// not be confirmed (<c>Paymob.RefundOutcomeUnknown</c>, or a call that was
    /// cancelled or timed out before its outcome could be read). Requires the
    /// pre-call claim: the marker may only be raised against a persisted
    /// in-flight attempt, which is what makes the state durable across a
    /// crash and exclusive under concurrency. The attempt is recorded —
    /// provider reason, instant, award — but no money is booked and the
    /// refund state is left untouched, so an unconfirmed movement is never
    /// counted as a completed one. The in-flight claim and its attempt
    /// identity fold into the marker until it is reconciled through
    /// <see cref="ReconcileAmbiguousRefund"/>. The recorded fields describe
    /// the <b>latest</b> ambiguous attempt: a second ambiguous attempt after
    /// an intervening reconciliation overwrites the previous attempt's
    /// record, so readers must key on <see cref="IsRefundOutcomeAmbiguous"/> —
    /// the authoritative open-question gate — and never on the audit fields
    /// alone.
    /// </summary>
    public void MarkRefundOutcomeAmbiguous(string reason, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.RefundPending || RefundClaimedOnUtc is null
            || RefundAmount is null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (IsRefundOutcomeAmbiguous)
            throw new InvalidOperationException("CareHomes.Bookings.RefundAlreadyAmbiguous");
        IsRefundOutcomeAmbiguous = true;
        RefundAmbiguityReason = reason;
        RefundAmbiguityRaisedOnUtc = utcNow;
        RefundAmbiguityAwardAmount = RefundAmount;
        Touch(utcNow);
    }

    /// <summary>
    /// An operator's evidence-based reconciliation of the ambiguity marker
    /// (decision #17). <paramref name="refundMoved"/> states what was
    /// verified, not guessed: a verified movement completes the refund for
    /// exactly the recorded ambiguous award — once, never re-decided or
    /// re-sent — and a verified non-payment returns the booking to a failed,
    /// retryable state (the <em>only</em> way a refund becomes retryable
    /// after an ambiguous attempt). Any other evidence state cannot clear the
    /// gate: this method refuses unless the marker stands.
    /// <para>
    /// Because the marker proves the provider call <em>returned</em> —
    /// uncertainly, but it returned — this reconciliation cannot race a live
    /// provider send. It is still fenced twice: against the operator's own
    /// stale view and against a newer attempt through
    /// <paramref name="expectedAttemptClaimId"/>, which must equal the
    /// booking's current in-flight attempt identity, plus the optimistic
    /// version token the persistence layer enforces.
    /// </para>
    /// </summary>
    public void ReconcileAmbiguousRefund(
        string reconciledBy,
        bool refundMoved,
        string? verifiedRefundReference,
        Guid expectedAttemptClaimId,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reconciledBy))
            throw new ArgumentException("A reconciliation must name the operator who performed it.");
        if (!IsRefundOutcomeAmbiguous || Status != CareHomeBookingStatus.RefundPending)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (RefundAttemptClaimId != expectedAttemptClaimId)
            throw new InvalidOperationException("CareHomes.Bookings.RefundAttemptMismatch");
        if (refundMoved && string.IsNullOrWhiteSpace(verifiedRefundReference))
            throw new ArgumentException("A verified refund movement must carry the provider's refund reference.");

        if (refundMoved)
            CompleteReconciledRefund(verifiedRefundReference!.Trim(), utcNow);
        else
            ReturnToVerifiedFailedRefund("Verified non-payment by reconciliation: the provider did not move the refund.", utcNow);

        ClearAmbiguityMarkers(reconciledBy.Trim(), utcNow);
    }

    /// <summary>
    /// Reconciliation of an interrupted claim — the crash window where the
    /// claim was committed before the provider call but no outcome was ever
    /// recorded. Deliberately <b>not reachable from the operator surface</b>:
    /// with no confirmed provider outcome on the row, nothing inside this
    /// module can prove the provider call is no longer in flight (elapsed
    /// age is not evidence), so clearing the claim here could race a live
    /// send into a second refund. It stays as the seam the queued provider
    /// attempt-liveness contract will call once an attempt can be proven
    /// dead, under the same evidence classes and the same
    /// <paramref name="expectedAttemptClaimId"/> compare-and-swap as
    /// <see cref="ReconcileAmbiguousRefund"/>; the persistence version token
    /// fences it against any state change that lands meanwhile.
    /// </summary>
    public void ReconcileInterruptedRefundClaim(
        string reconciledBy,
        bool refundMoved,
        string? verifiedRefundReference,
        Guid? expectedAttemptClaimId,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reconciledBy))
            throw new ArgumentException("A reconciliation must name the operator who performed it.");
        if (Status != CareHomeBookingStatus.RefundPending || RefundClaimedOnUtc is null
            || IsRefundOutcomeAmbiguous || RefundStatus != CareHomeRefundStatus.Pending)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (RefundAttemptClaimId != expectedAttemptClaimId)
            throw new InvalidOperationException("CareHomes.Bookings.RefundAttemptMismatch");
        if (refundMoved && string.IsNullOrWhiteSpace(verifiedRefundReference))
            throw new ArgumentException("A verified refund movement must carry the provider's refund reference.");

        if (refundMoved)
            CompleteReconciledRefund(verifiedRefundReference!.Trim(), utcNow);
        else
            ReturnToVerifiedFailedRefund("Verified non-payment by reconciliation: the provider did not move the refund.", utcNow);

        ClearAmbiguityMarkers(reconciledBy.Trim(), utcNow);
    }

    private void CompleteReconciledRefund(string verifiedRefundReference, DateTime utcNow)
    {
        RefundReference ??= verifiedRefundReference;
        RefundStatus = CareHomeRefundStatus.Completed;
        PaymentStatus = CareHomeBookingPaymentStatus.Refunded;
        Status = CareHomeBookingStatus.Refunded;
        RefundCompletedOnUtc = utcNow;
        // The verified completion discharges the held claim: the attempt is
        // over, and leaving it claimed would freeze the booking's refund
        // state forever. The ambiguity history (reason, raised instant,
        // award) is deliberately retained below as evidence.
        RefundClaimedOnUtc = null;
        RefundAttemptClaimId = null;
        Touch(utcNow);
    }

    private void ReturnToVerifiedFailedRefund(string reason, DateTime utcNow)
    {
        RefundStatus = CareHomeRefundStatus.Failed;
        RefundFailureReason = reason;
        RefundClaimedOnUtc = null;
        RefundAttemptClaimId = null;
        Touch(utcNow);
    }

    /// <summary>
    /// Clears the operator-facing ambiguity marker on a reconciled attempt.
    /// The latest attempt's evidence — why it stood
    /// (<see cref="RefundAmbiguityReason"/>), when it was raised
    /// (<see cref="RefundAmbiguityRaisedOnUtc"/>), and the exact award it was
    /// moving (<see cref="RefundAmbiguityAwardAmount"/>) — is deliberately
    /// <b>retained</b>, not nulled: it is the audit record of what the
    /// operator last reconciled (a later ambiguous attempt overwrites it with
    /// that attempt's record), and the only figure a movement on this booking
    /// could be re-verified against. Only the cleared-by and cleared-on
    /// fields are written, so <see cref="IsRefundOutcomeAmbiguous"/> alone —
    /// the field every reader must key on — answers "is there an open
    /// question".
    /// </summary>
    private void ClearAmbiguityMarkers(string reconciledBy, DateTime utcNow)
    {
        IsRefundOutcomeAmbiguous = false;
        RefundAmbiguityClearedOnUtc = utcNow;
        RefundAmbiguityClearedBy = reconciledBy;
        Touch(utcNow);
    }

    public bool TryClaimPaymentIntent(DateTime utcNow)
    {
        if (PaymobOrderId is not null || PaymentIntentClaimedOnUtc is not null) return false;
        PaymentIntentClaimedOnUtc = utcNow; Touch(utcNow); return true;
    }

    public void SetPaymentIntent(int method, string paymobOrderId, string intentionOrderId, string clientSecret, string publicKey, DateTime utcNow)
    { PaymentIntentMethod = method; PaymobOrderId = paymobOrderId; IntentionOrderId = intentionOrderId; PaymentClientSecret = clientSecret; PaymentPublicKey = publicKey; PaymentIntentClaimedOnUtc = null; Touch(utcNow); }

    public void ReleasePaymentIntentClaim(DateTime utcNow)
    { PaymentIntentClaimedOnUtc = null; Touch(utcNow); }

    public void MarkRefundInitiated(string? refundReference, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.RefundPending || RefundClaimedOnUtc is null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        RefundReference = refundReference;
        RefundStatus = CareHomeRefundStatus.Initiated;
        RefundClaimedOnUtc = null;
        RefundAttemptClaimId = null;
        PaymentStatus = CareHomeBookingPaymentStatus.RefundInitiated;
        Status = CareHomeBookingStatus.RefundInitiated;
        Touch(utcNow);
    }

    public bool MarkRefundCompleted(string? refundReference, DateTime utcNow)
    {
        if (RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted) return false;
        if (RefundStatus != CareHomeRefundStatus.Initiated
            || string.IsNullOrWhiteSpace(RefundReference)
            || !string.Equals(RefundReference, refundReference, StringComparison.Ordinal)) return false;
        CompleteRefund(utcNow);
        return true;
    }

    public bool MarkRefundCompletedFromProviderCallback(string? refundReference, long transactionId, DateTime utcNow)
    {
        if (RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted
            || RefundStatus != CareHomeRefundStatus.Initiated
            || string.IsNullOrWhiteSpace(RefundReference)) return false;
        bool matchesRefund = string.Equals(RefundReference, refundReference, StringComparison.Ordinal);
        bool matchesPayment = PaymobTransactionId.HasValue && PaymobTransactionId.Value == transactionId;
        if (!matchesRefund && !matchesPayment) return false;
        CompleteRefund(utcNow);
        return true;
    }

    private void CompleteRefund(DateTime utcNow)
    {
        RefundStatus = CareHomeRefundStatus.Completed;
        PaymentStatus = CareHomeBookingPaymentStatus.Refunded;
        Status = CareHomeBookingStatus.Refunded;
        RefundCompletedOnUtc = utcNow;
        Touch(utcNow);
    }

    public void MarkRefundManuallyCompleted(string reference, string reason, UserId actor, DateTime utcNow)
    {
        if (RefundStatus is not (CareHomeRefundStatus.Pending or CareHomeRefundStatus.Failed or CareHomeRefundStatus.Initiated)
            || RefundClaimedOnUtc is not null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Refund reference and reason are required.");
        RefundReference = reference.Trim();
        RefundFailureReason = null;
        RefundStatus = CareHomeRefundStatus.ManuallyCompleted;
        PaymentStatus = CareHomeBookingPaymentStatus.Refunded;
        Status = CareHomeBookingStatus.Refunded;
        RefundCompletedOnUtc = utcNow;
        RefundCompletedBy = actor;
        RefundClaimedOnUtc = null;
        DecisionReason = reason.Trim();
        Touch(utcNow);
    }

    private void Touch(DateTime utcNow) { UpdatedOnUtc = utcNow; Version++; }

    private static TimeZoneInfo GetCairoTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }

    public void AssignPhysicalResource(Guid roomId, Guid? bedId, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.Accepted) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (AssignedRoomId is not null || ActualCheckInOnUtc is not null) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (roomId == Guid.Empty || (bedId is Guid b && b == Guid.Empty)) throw new ArgumentException("A physical room is required.");
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.");
        AssignedRoomId = roomId; AssignedBedId = bedId; Touch(utcNow);
    }

    public void TransferPhysicalResource(Guid roomId, Guid? bedId, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.Accepted || AssignedRoomId is null || ActualCheckOutOnUtc is not null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (roomId == Guid.Empty || (bedId is Guid b && b == Guid.Empty))
            throw new ArgumentException("A physical room is required.");
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.");
        AssignedRoomId = roomId;
        AssignedBedId = bedId;
        Touch(utcNow);
    }

    public void RecordCheckIn(UserId actor, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.Accepted || AssignedRoomId is null) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.");
        if (ActualCheckInOnUtc is not null || ActualCheckOutOnUtc is not null) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        ActualCheckInOnUtc = utcNow; ActualCheckInRecordedBy = actor; Touch(utcNow);
    }

    public void ConfirmFamilyCheckIn(UserId actor, DateTime utcNow)
    {
        if (ActualCheckInOnUtc is null || ActualCheckOutOnUtc is not null) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.");
        FamilyCheckInConfirmedOnUtc ??= utcNow; FamilyCheckInConfirmedBy ??= actor; Touch(utcNow);
    }

    public void RecordCheckOut(UserId actor, DateTime utcNow)
    {
        if (ActualCheckInOnUtc is null || ActualCheckOutOnUtc is not null) throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        if (utcNow.Kind != DateTimeKind.Utc || utcNow < ActualCheckInOnUtc) throw new ArgumentException("A valid checkout timestamp is required.");
        ActualCheckOutOnUtc = utcNow; ActualCheckOutRecordedBy = actor; Touch(utcNow);
    }
}
