using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;

namespace Sanad.Modules.Families.Domain.Bookings;

public sealed class Booking : AggregateRoot<BookingId>
{
    private readonly List<PaymentTransaction> _paymentTransactions = [];
    private readonly List<BookingSessionAllocation> _sessionAllocations = [];

    public const int MaximumAddressLength = 500;
    public const int MaximumInstructionsLength = 1000;
    public const int MaximumNotesLength = 2000;
    public const int MaximumReasonLength = 500;

    private Booking()
    {
    }

    private Booking(
        BookingId id,
        FamilyId familyId,
        UserId createdByUserId,
        ElderlyId elderlyId,
        CaregiverId caregiverId,
        BookingCaregiverType caregiverType,
        BookingShiftType shiftType,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        string serviceAddress,
        string? specialInstructions,
        BookingPriceSnapshot priceSnapshot,
        DateTime acceptanceDeadlineUtc,
        DateTime createdOnUtc,
        Guid? bookingAreaId = null,
        Guid? bookingServiceId = null,
        BookingRefundPolicyVersion? refundPolicyVersion = null)
        : base(id)
    {
        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        ElderlyId = elderlyId;
        CaregiverId = caregiverId;
        CaregiverType = caregiverType;
        ShiftType = shiftType;
        BookingDate = bookingDate;
        StartTime = startTime;
        EndTime = endTime;
        ServiceAddress = serviceAddress;
        SpecialInstructions = specialInstructions;
        PriceSnapshot = priceSnapshot;
        BookingAreaId = bookingAreaId;
        BookingServiceId = bookingServiceId;
        AcceptanceDeadlineUtc = acceptanceDeadlineUtc;
        Status = BookingStatus.PendingPayment;

        // The sold revision is snapshotted here and never recomputed. An omitted value captures the
        // legacy revision, so existing call sites and any historical row keep the policy they were
        // created under instead of being silently repriced by a later revision.
        BookingRefundPolicyVersion sold = refundPolicyVersion
            ?? BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: false);
        RefundPolicyVersion = sold.Version;
        RefundPolicyDocumentEffectiveOn = sold.DocumentEffectiveOnUtc;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FamilyId FamilyId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public CaregiverId CaregiverId { get; private set; }
    public BookingCaregiverType CaregiverType { get; private set; }
    public BookingShiftType ShiftType { get; private set; }
    public DateOnly BookingDate { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public string ServiceAddress { get; private set; } = string.Empty;
    public string? SpecialInstructions { get; private set; }

    /// <summary>
    /// Chat Plus immutable pricing attribution: the caller-selected booked
    /// area/service recorded together at creation. Nullable for legacy
    /// bookings (which contribute no pricing sample); when supplied, both
    /// are required together and never change afterwards. Never backfilled
    /// from current caregiver coverage or free-text address.
    /// </summary>
    public Guid? BookingAreaId { get; private set; }

    /// <summary>See <see cref="BookingAreaId"/>.</summary>
    public Guid? BookingServiceId { get; private set; }
    public BookingPriceSnapshot PriceSnapshot { get; private set; } = default!;
    public DateTime AcceptanceDeadlineUtc { get; private set; }
    public BookingStatus Status { get; private set; }

    /// <summary>
    /// The refund-policy revision this booking was sold under, preserved at booking time (Owner
    /// decisions #7 and #8). A booking created before the new policy was activated keeps
    /// <see cref="BookingRefundPolicyVersion.LegacyVersion"/> and continues to dispatch to
    /// <see cref="BookingCancellationPolicy"/>; a later policy revision never retroactively
    /// reprices an existing booking.
    /// </summary>
    public int RefundPolicyVersion { get; private set; } = BookingRefundPolicyVersion.LegacyVersion;

    /// <summary>
    /// The approved refund-document date the sold revision came from, recorded beside the booking
    /// so a reviewer can see which approved text applied (decision #7). Display and attribution
    /// only; it never participates in money arithmetic.
    /// </summary>
    public DateOnly RefundPolicyDocumentEffectiveOn { get; private set; }

    /// <summary>
    /// Money already refunded against this booking, in piastres. The policy-vocabulary half of
    /// decision #4: the refundable ceiling is captured money less this value, and it rises only
    /// through <see cref="RecordRefundedAmount"/>.
    /// </summary>
    public long RefundedMinorUnits { get; private set; }

    /// <summary>
    /// True while a refund attempt on this booking ended in an outcome the gateway could not
    /// confirm (Paymob <c>RefundOutcomeUnknown</c>): the money may or may not have moved, and the
    /// system must not guess. Decision #17 is answered here rather than by a retry — while this
    /// marker stands, <see cref="EnsureRefundAttemptAllowed"/> refuses every further refund
    /// attempt on the booking, so blind retries cannot double-refund a customer whose first
    /// refund silently succeeded.
    /// </summary>
    public bool IsRefundOutcomeAmbiguous { get; private set; }

    /// <summary>The attempt whose outcome was unconfirmed, for the reconciliation report.</summary>
    public string? RefundAmbiguityReference { get; private set; }

    /// <summary>
    /// Why the marker was raised, in the provider's own error code (for example
    /// <c>Paymob.RefundOutcomeUnknown</c>). Kept so an operator knows what to reconcile against
    /// instead of only that something was ambiguous.
    /// </summary>
    public string? RefundAmbiguityReason { get; private set; }

    /// <summary>When the unconfirmed attempt was made, in UTC.</summary>
    public DateTime? RefundAmbiguityRaisedOnUtc { get; private set; }

    /// <summary>
    /// The award the unconfirmed attempt was trying to move, in piastres. Recorded so a
    /// reconciliation that finds the money did move can book exactly that amount, and one that
    /// finds it did not can re-attempt it, without re-deciding the cancellation.
    /// </summary>
    public long? RefundAmbiguityAwardMinorUnits { get; private set; }

    /// <summary>When an operator cleared the marker, in UTC. Null while it stands.</summary>
    public DateTime? RefundAmbiguityClearedOnUtc { get; private set; }

    /// <summary>
    /// The operator who cleared the marker, and the evidence they recorded. Both are cleared
    /// together with the marker and preserved here afterwards, so the reconciliation that
    /// unblocked the booking stays attributable.
    /// </summary>
    public string? RefundAmbiguityClearedBy { get; private set; }

    /// <summary>
    /// Optimistic concurrency token for the whole booking row, following the house pattern used by
    /// <c>FamilySubscriptionConfiguration.LifecycleVersion</c>: a plain counter marked
    /// <c>IsConcurrencyToken</c>. It moves on every refund-attempt state change, so a second
    /// request that loaded the same booking can never persist its own claim: its update fails the
    /// version comparison at the database boundary and it must not call the provider.
    /// </summary>
    public int RefundAttemptVersion { get; private set; }

    /// <summary>
    /// Correlation id of the refund attempt currently claimed, or of the last attempt whose claim
    /// was released. This is the value an operator reconciles against and the value a settlement
    /// call must present to prove it holds the claim.
    /// </summary>
    public Guid? RefundAttemptClaimId { get; private set; }

    /// <summary>When the in-flight claim was acquired, in UTC.</summary>
    public DateTime? RefundAttemptClaimedOnUtc { get; private set; }

    /// <summary>
    /// The exact award the in-flight claim is moving, in piastres. Persisted <em>before</em> any
    /// external call, so a crash between the claim and the provider call — or between the provider
    /// call and the result save — leaves the amount that may have moved on the record instead of
    /// leaving it only in memory.
    /// </summary>
    public long? RefundAttemptAwardMinorUnits { get; private set; }

    /// <summary>
    /// True between a persisted refund claim and its recorded outcome: the claim has been committed
    /// but the provider result has not. While it stands no other refund attempt may start and no
    /// retry may fire — an interrupted attempt is uncertain, not automatically re-sendable.
    /// </summary>
    public bool IsRefundAttemptInFlight { get; private set; }

    public string? PaymobOrderId { get; private set; }
    public string? PaymobTransactionId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CaregiverNotes { get; private set; }
    public string? PaymobRefundTransactionId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PaidOnUtc { get; private set; }
    public DateTime? ConfirmedOnUtc { get; private set; }
    public DateTime? StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public DateTime? CancelledOnUtc { get; private set; }
    public DateTime? ExpiredOnUtc { get; private set; }
    public DateTime? RefundedOnUtc { get; private set; }

    public IReadOnlyCollection<PaymentTransaction> PaymentTransactions => _paymentTransactions.AsReadOnly();

    /// <summary>
    /// Per-session allocations of a discounted multi-session package (Owner decision #5). Empty for
    /// an ordinary single booking. Exposed read-only, and mutated only through
    /// <see cref="RecordSessionDelivery"/>, which cannot change an allocated amount.
    /// </summary>
    public IReadOnlyList<BookingSessionAllocation> SessionAllocations => _sessionAllocations.AsReadOnly();

    /// <summary>
    /// Records the delivery of one session in a package, returning the new allocation so a caller
    /// can persist it. A delivered session becomes non-refundable (clause 3) and its delivery
    /// instant is kept as evidence, but its allocated amount is never altered.
    /// <para>
    /// Whole and partial delivery use this same method: delivering every session records each one,
    /// while a partially delivered package simply leaves the remaining sessions
    /// <see cref="BookingSessionOutcome.NotDelivered"/>. No separate "partially delivered" flag is
    /// kept, because the per-session outcomes already state that exactly.
    /// </para>
    /// </summary>
    public BookingSessionAllocation RecordSessionDelivery(
        int sessionIndex,
        DateTime deliveredOnUtc,
        DateTime utcNow)
    {
        if (_sessionAllocations.Count == 0)
            throw new DomainException("This booking has no package sessions to record a delivery for.");

        int position = _sessionAllocations.FindIndex(
            allocation => allocation.SessionIndex == sessionIndex);

        if (position < 0)
            throw new DomainException($"Session {sessionIndex} is not part of this booking.");

        BookingSessionAllocation delivered = _sessionAllocations[position].MarkDelivered(deliveredOnUtc);
        _sessionAllocations[position] = delivered;

        UpdatedOnUtc = utcNow;
        return delivered;
    }

    /// <summary>
    /// Records that the provider failed to deliver one session (cancellation, no-show or excessive
    /// lateness), making that session refundable in full under clause 4. The allocated amount is
    /// preserved.
    /// </summary>
    public BookingSessionAllocation RecordSessionProviderFailure(int sessionIndex, DateTime utcNow)
    {
        if (_sessionAllocations.Count == 0)
            throw new DomainException("This booking has no package sessions to record a failure for.");

        int position = _sessionAllocations.FindIndex(
            allocation => allocation.SessionIndex == sessionIndex);

        if (position < 0)
            throw new DomainException($"Session {sessionIndex} is not part of this booking.");

        BookingSessionAllocation failed = _sessionAllocations[position].MarkProviderFailed();
        _sessionAllocations[position] = failed;

        UpdatedOnUtc = utcNow;
        return failed;
    }

    /// <summary>
    /// Attaches the session allocations of a discounted package to this booking, once, at
    /// purchase. It accepts only the exact set <see cref="BookingRefundPolicy.AllocatePackageSessions"/>
    /// produces: a dense 1..N set whose amounts sum exactly to the paid package total (decision #5).
    /// A second call is refused, so allocations cannot be rewritten after the sale.
    /// </summary>
    public void AttachSessionAllocations(long paidPackageMinorUnits, int sessionCount)
    {
        if (_sessionAllocations.Count > 0)
            throw new DomainException("This booking already has its package sessions allocated.");

        if (Status != BookingStatus.PendingPayment)
            throw new DomainException(
                "Package sessions are allocated at purchase, before the booking is paid.");

        _sessionAllocations.AddRange(
            BookingRefundPolicy.AllocatePackageSessions(paidPackageMinorUnits, sessionCount));
    }

    public static Booking Create(
        FamilyId familyId,
        UserId createdByUserId,
        ElderlyId elderlyId,
        CaregiverId caregiverId,
        BookingCaregiverType caregiverType,
        BookingShiftType shiftType,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        string serviceAddress,
        string? specialInstructions,
        BookingPriceSnapshot priceSnapshot,
        DateTime acceptanceDeadlineUtc,
        DateOnly currentDate,
        DateTime createdOnUtc,
        Guid? bookingAreaId = null,
        Guid? bookingServiceId = null,
        BookingRefundPolicyVersion? refundPolicyVersion = null)
    {
        if (familyId == FamilyId.Empty)
            throw new DomainException("Family ID is required.");

        if (createdByUserId == UserId.Empty)
            throw new DomainException("User ID is required.");

        if (elderlyId == ElderlyId.Empty)
            throw new DomainException("Elderly dependent ID is required.");

        if (caregiverId == CaregiverId.Empty)
            throw new DomainException("Caregiver ID is required.");

        if (!Enum.IsDefined(caregiverType))
            throw new DomainException("Booking caregiver type is invalid.");

        if (bookingDate < currentDate)
            throw new DomainException("Booking date cannot be in the past.");

        if (string.IsNullOrWhiteSpace(serviceAddress))
            throw new DomainException("Service address is required.");

        string normalizedAddress = serviceAddress.Trim();
        if (normalizedAddress.Length > MaximumAddressLength)
            throw new DomainException($"Service address cannot exceed {MaximumAddressLength} characters.");

        string? normalizedInstructions = null;
        if (!string.IsNullOrWhiteSpace(specialInstructions))
        {
            normalizedInstructions = specialInstructions.Trim();
            if (normalizedInstructions.Length > MaximumInstructionsLength)
                throw new DomainException($"Special instructions cannot exceed {MaximumInstructionsLength} characters.");
        }

        if (acceptanceDeadlineUtc <= createdOnUtc)
            throw new DomainException("Acceptance deadline must be after the creation time.");

        // Pricing attribution: both booked dimensions together or neither;
        // empties are malformed, not legacy omissions.
        if (bookingAreaId.HasValue != bookingServiceId.HasValue)
            throw new DomainException("Booked area and service must be supplied together.");

        if ((bookingAreaId.HasValue && bookingAreaId.Value == Guid.Empty) ||
            (bookingServiceId.HasValue && bookingServiceId.Value == Guid.Empty))
            throw new DomainException("Booked area and service cannot be empty.");

        return new Booking(
            BookingId.New(),
            familyId,
            createdByUserId,
            elderlyId,
            caregiverId,
            caregiverType,
            shiftType,
            bookingDate,
            startTime,
            endTime,
            normalizedAddress,
            normalizedInstructions,
            priceSnapshot,
            acceptanceDeadlineUtc,
            createdOnUtc,
            bookingAreaId,
            bookingServiceId,
            refundPolicyVersion);
    }

    public void MarkAsPaid(string paymobOrderId, string paymobTransactionId, DateTime utcNow)
    {
        if (Status != BookingStatus.PendingPayment)
            throw new DomainException("Only bookings in PendingPayment status can be marked as paid.");


        PaymobOrderId = paymobOrderId;
        PaymobTransactionId = paymobTransactionId;
        Status = BookingStatus.PendingCaregiverApproval;
        PaidOnUtc = utcNow;
        UpdatedOnUtc = utcNow;

        foreach (var transaction in _paymentTransactions)
        {
            if (transaction.PaymobOrderId == paymobOrderId)
            {
                transaction.MarkSucceeded(paymobTransactionId, utcNow);
            }
        }
    }

    public void RecordPaymentIntent(
    string paymobOrderId,
    PaymentMethod method,
    DateTime utcNow)
    {
        if (Status != BookingStatus.PendingPayment)
            throw new DomainException("Only bookings in PendingPayment status can start a payment.");

        if (string.IsNullOrWhiteSpace(paymobOrderId))
            throw new DomainException("Paymob order id is required.");

        if (!Enum.IsDefined(method))
            throw new DomainException("Payment method is invalid.");

        if (_paymentTransactions.Any(t => t.PaymobOrderId == paymobOrderId && t.Status == PaymentTransactionStatus.Pending))
            throw new DomainException("A pending payment attempt already exists for this order.");

        _paymentTransactions.Add(
            PaymentTransaction.Create(
                paymobOrderId.Trim(),
                method,
                PriceSnapshot.TotalPayableAmount,
                PriceSnapshot.Currency,
                utcNow));

        UpdatedOnUtc = utcNow;
    }

    public void RecordPaymentFailure(
        string paymobOrderId,
        string? paymobTransactionId,
        DateTime utcNow)
    {
        if (Status != BookingStatus.PendingPayment)
            throw new DomainException("Only pending bookings can record a payment failure.");

        foreach (PaymentTransaction transaction in _paymentTransactions)
        {
            if (transaction.PaymobOrderId == paymobOrderId)
            {
                transaction.MarkFailed(paymobTransactionId, utcNow);
            }
        }

        UpdatedOnUtc = utcNow;
    }

    public void MarkRefunded(string? paymobRefundTransactionId, DateTime utcNow)
    {
        if (Status is not (BookingStatus.DeclinedByCaregiver
            or BookingStatus.Expired
            or BookingStatus.CancelledByFamily
            or BookingStatus.CancelledByCaregiver))
        {
            throw new DomainException("Only ended bookings can be marked as refunded.");
        }

        if (Status == BookingStatus.Refunded)
            throw new DomainException("This booking is already refunded.");

        PaymobRefundTransactionId = paymobRefundTransactionId;
        Status = BookingStatus.Refunded;
        RefundedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;

        foreach (PaymentTransaction transaction in _paymentTransactions)
        {
            transaction.MarkRefunded(utcNow);
        }
    }

    /// <summary>
    /// Records refunded money against this booking, in piastres. This is the only writer of
    /// <see cref="RefundedMinorUnits"/>, and it accepts only a positive amount — a zero-award
    /// cancellation (decision #31) records nothing, and a negative "refund" is meaningless. It
    /// deliberately does not need the booking to be in a particular status: the ledger, not the
    /// booking status, is the money record, and the whole-booking
    /// <see cref="BookingStatus.Refunded"/> transition remains a separate, stricter act.
    /// </summary>
    /// <remarks>
    /// The decision #4 ceiling is enforced here as well as in the policy. The policy already
    /// clamps every award, so this guard is defence in depth: a caller that reaches this method
    /// without a policy decision (an ops reconciliation, a settlement seam) still cannot push
    /// cumulative refunds past the captured amount.
    /// </remarks>
    public void RecordRefundedAmount(long refundedMinorUnits, DateTime utcNow)
    {
        if (refundedMinorUnits <= 0)
            throw new DomainException("A recorded refund amount must be greater than zero piastres.");

        if (refundedMinorUnits > RefundableCeilingMinorUnits())
            throw new DomainException(
                "Recorded refunds cannot exceed the captured amount less previous refunds.");

        RefundedMinorUnits = checked(RefundedMinorUnits + refundedMinorUnits);
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// The most that may still be refunded: the captured amount less what was already refunded,
    /// never below zero. Owner decision #4 ("cumulative refunds cannot exceed captured funds less
    /// previous refunds") is expressed here as the ceiling a policy award is clamped to.
    /// </summary>
    public long RefundableCeilingMinorUnits()
    {
        long captured = BookingRefundPolicyInput.CapturedMinorUnitsOf(this);
        return Math.Max(0L, captured - RefundedMinorUnits);
    }

    /// <summary>
    /// Refuses every refund attempt while an unconfirmed one stands (Owner decision #17), and while
    /// a claimed attempt is still in flight. The question these guards answer is not "should this
    /// refund happen" but "did the last one already happen, or may it be happening right now" — and
    /// until the answer is known, the only safe answer is to move no money.
    /// </summary>
    public void EnsureRefundAttemptAllowed()
    {
        if (IsRefundOutcomeAmbiguous)
            throw new DomainException(
                "A previous refund attempt on this booking has an unconfirmed outcome; an administrator must reconcile it before any further refund is attempted.");

        if (IsRefundAttemptInFlight)
            throw new DomainException(
                "A refund attempt on this booking is already claimed and has not recorded its outcome; an administrator must reconcile it before any further refund is attempted.");
    }

    /// <summary>
    /// Acquires the booking's single in-flight refund claim. The caller must persist this state
    /// <em>before</em> sending anything to the provider: the commit is what makes the claim
    /// exclusive, because a competing request holding a stale concurrency token fails its own
    /// commit and therefore never reaches the provider.
    /// <para>
    /// Nothing is claimed when an earlier attempt is unconfirmed or still in flight: both are
    /// uncertain, and an uncertain attempt is reconciled by an operator, never re-sent.
    /// </para>
    /// </summary>
    public void ClaimRefundAttempt(Guid claimId, long awardedMinorUnits, DateTime utcNow)
    {
        if (claimId == Guid.Empty)
            throw new DomainException("A refund attempt claim must carry a correlation id.");

        if (awardedMinorUnits <= 0)
            throw new DomainException("A refund attempt must carry a positive award.");

        EnsureRefundAttemptAllowed();

        if (awardedMinorUnits > RefundableCeilingMinorUnits())
            throw new DomainException(
                "A refund attempt cannot exceed the captured amount less previous refunds.");

        RefundAttemptClaimId = claimId;
        RefundAttemptClaimedOnUtc = utcNow;
        RefundAttemptAwardMinorUnits = awardedMinorUnits;
        IsRefundAttemptInFlight = true;
        RefundAttemptVersion = checked(RefundAttemptVersion + 1);
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Releases the in-flight claim once its outcome is recorded — on a confirmed settlement (after
    /// <see cref="RecordRefundedAmount"/>) or on a definitive provider refusal that proves no money
    /// moved, which is what permits a bounded retry. The claim id and award are kept as the audit
    /// trail. An interrupted attempt is never released this way: it is left in flight, or converted
    /// to the ambiguity marker, so it can only be cleared by evidence-based reconciliation.
    /// </summary>
    public void ReleaseRefundAttempt(DateTime utcNow)
    {
        if (!IsRefundAttemptInFlight)
            return;

        IsRefundAttemptInFlight = false;
        RefundAttemptVersion = checked(RefundAttemptVersion + 1);
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Raises the ambiguity marker after a provider call whose outcome could not be confirmed.
    /// The attempt is recorded — reference, provider reason, instant, award — but no money is
    /// booked: the ledger is deliberately left untouched so an unconfirmed movement is never
    /// counted as a completed one, and the ceiling therefore stays where it was. The in-flight
    /// claim is folded into the marker, because the marker is the stricter of the two.
    /// </summary>
    public void MarkRefundOutcomeAmbiguous(
        string? attemptReference,
        string reason,
        DateTime utcNow,
        long awardedMinorUnits)
    {
        if (IsRefundOutcomeAmbiguous)
            throw new DomainException(
                "This booking already carries an unconfirmed refund attempt.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An unconfirmed refund attempt must record why it is ambiguous.");

        if (awardedMinorUnits <= 0)
            throw new DomainException("An unconfirmed refund attempt must record the award it was moving.");

        IsRefundOutcomeAmbiguous = true;
        RefundAmbiguityReference = attemptReference;
        RefundAmbiguityReason = reason;
        RefundAmbiguityRaisedOnUtc = utcNow;
        RefundAmbiguityAwardMinorUnits = awardedMinorUnits;
        IsRefundAttemptInFlight = false;
        RefundAttemptVersion = checked(RefundAttemptVersion + 1);
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Clears the marker on an operator's reconciliation, which is what reopens refund attempts
    /// on the booking. The clearing operator and instant are kept as the durable record of who
    /// unblocked the booking and when.
    /// </summary>
    public void ClearRefundAmbiguity(string reconciledBy, DateTime utcNow)
    {
        if (!IsRefundOutcomeAmbiguous)
            throw new DomainException("This booking has no unconfirmed refund attempt to reconcile.");

        if (string.IsNullOrWhiteSpace(reconciledBy))
            throw new DomainException("A reconciliation must name the operator who performed it.");

        IsRefundOutcomeAmbiguous = false;
        RefundAmbiguityClearedOnUtc = utcNow;
        RefundAmbiguityClearedBy = reconciledBy;
        RefundAttemptVersion = checked(RefundAttemptVersion + 1);
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Clears an interrupted claim that never reached the marker — the crash-before-send window,
    /// where the claim was committed but no provider result exists. Only an operator's
    /// reconciliation may call this, and only with a recorded decision about whether the money
    /// moved (the command decides that; this method just enforces that a claim exists).
    /// </summary>
    public void ClearInterruptedRefundAttempt(string reconciledBy, DateTime utcNow)
    {
        if (!IsRefundAttemptInFlight)
            throw new DomainException("This booking has no in-flight refund attempt to reconcile.");

        if (string.IsNullOrWhiteSpace(reconciledBy))
            throw new DomainException("A reconciliation must name the operator who performed it.");

        IsRefundAttemptInFlight = false;
        RefundAmbiguityClearedOnUtc = utcNow;
        RefundAmbiguityClearedBy = reconciledBy;
        RefundAttemptVersion = checked(RefundAttemptVersion + 1);
        UpdatedOnUtc = utcNow;
    }

    public void AcceptByCaregiver(DateTime utcNow)
    {
        if (Status != BookingStatus.PendingCaregiverApproval)
            throw new DomainException("Only paid bookings awaiting approval can be accepted.");

        if (utcNow > AcceptanceDeadlineUtc)
            throw new DomainException("The acceptance window for this booking has expired.");

        Status = BookingStatus.Confirmed;
        ConfirmedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void DeclineByCaregiver(string reason, DateTime utcNow)
    {
        if (Status != BookingStatus.PendingCaregiverApproval)
            throw new DomainException("Only paid bookings awaiting approval can be declined.");

        Status = BookingStatus.DeclinedByCaregiver;
        CancellationReason = NormalizeText(reason, MaximumReasonLength, "Decline reason");
        CancelledOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void Expire(DateTime utcNow)
    {
        if (Status is not (BookingStatus.PendingPayment or BookingStatus.PendingCaregiverApproval))
            throw new DomainException("Only pending bookings can expire.");

        if (utcNow <= AcceptanceDeadlineUtc)
            throw new DomainException("The acceptance window has not expired yet.");

        Status = BookingStatus.Expired;
        ExpiredOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void StartVisit(DateTime utcNow)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException("Only Confirmed bookings can be started.");

        if (ConfirmedOnUtc is null || utcNow < ConfirmedOnUtc.Value)
            throw new DomainException("A visit cannot start before the booking is confirmed.");

        Status = BookingStatus.InProgress;
        StartedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void CompleteVisit(string? caregiverNotes, DateTime utcNow)
    {
        if (Status != BookingStatus.InProgress)
            throw new DomainException("Only InProgress bookings can be completed.");

        if (StartedOnUtc is null || utcNow < StartedOnUtc.Value)
            throw new DomainException("A visit cannot be completed before it starts.");

        Status = BookingStatus.Completed;
        CaregiverNotes = NormalizeText(caregiverNotes, MaximumNotesLength, "Caregiver notes");
        CompletedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void CancelByFamily(string reason, DateTime utcNow)
    {
        if (Status is not (BookingStatus.PendingPayment or BookingStatus.PendingCaregiverApproval or BookingStatus.Confirmed))
            throw new DomainException("This booking can no longer be cancelled by the family.");

        Status = BookingStatus.CancelledByFamily;
        CancellationReason = NormalizeText(reason, MaximumReasonLength, "Cancellation reason");
        CancelledOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void CancelByCaregiver(string reason, DateTime utcNow)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException("Only Confirmed bookings can be cancelled by the caregiver.");

        Status = BookingStatus.CancelledByCaregiver;
        CancellationReason = NormalizeText(reason, MaximumReasonLength, "Cancellation reason");
        CancelledOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    private static string? NormalizeText(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        if (trimmed.Length > max)
            throw new DomainException($"{field} cannot exceed {max} characters.");
        return trimmed;
    }
}
