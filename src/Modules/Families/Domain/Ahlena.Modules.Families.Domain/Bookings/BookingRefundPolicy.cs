using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// The Owner-approved care-booking refund policy, revision 2, implemented from binding clauses 2,
/// 3 and 4 of the approved document (effective 2026-10-22).
/// <para>
/// <b>Rules, measured against the scheduled service start, never against provider acceptance:</b>
/// <list type="bullet">
/// <item><description>Clause 2, the customer cancels: at or before 24 hours before start, a full refund; strictly inside 24 hours but before start, 50%; at or after start, nothing.</description></item>
/// <item><description>Clause 2, inaccessibility: if the recipient could not be accessed at the appointed time, nothing is refunded.</description></item>
/// <item><description>Clause 3, multi-session: the cancellation rules apply per session; delivered sessions are non-refundable; unused sessions are refunded at their allocated discounted package price; a future session cancelled at least 24 hours ahead is refunded in full.</description></item>
/// <item><description>Clause 4, provider failure: provider cancellation, no-show, or lateness beyond 60 minutes without the customer's consent refunds that session in full.</description></item>
/// <item><description>Clause 4, Ahlena cancellation and force majeure: undelivered services and unused sessions are refunded; delivered services remain payable.</description></item>
/// <item><description>Everything before acceptance stays as it was: cancelling while unpaid owes nothing, and cancelling or rejecting while awaiting caregiver approval refunds the captured amount.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Deterministic and side-effect-free.</b> The policy reads no clock, calls no provider and
/// queries nothing. It refuses to decide rather than guess: a missing or non-UTC timestamp, a
/// malformed allocation set, an award that would exceed captured money less previous refunds, or a
/// state/actor pair it does not recognise all raise <see cref="DomainException"/>. Uncertainty is
/// never silently answered as "no refund due".
/// </para>
/// <para>
/// <b>One policy implementation, two revisions.</b> Revision 1 bookings must keep the
/// acceptance-relative 60-minute rule they were sold under, so v1 dispatch continues to use
/// <see cref="BookingCancellationPolicy"/>. This type serves revision 2 and stamps the booking's
/// own revision on every decision it produces, so a rescheduled or extended booking can never be
/// quietly repriced against a different policy.
/// </para>
/// </summary>
public static class BookingRefundPolicy
{
    /// <summary>Revision this policy implements.</summary>
    public const int CurrentPolicyVersion = BookingRefundPolicyVersion.ScheduledStartVersion;

    /// <summary>Clause 2: the window before the scheduled start in which cancellation is free.</summary>
    public static readonly TimeSpan FullRefundWindow = TimeSpan.FromHours(24);

    /// <summary>Clause 2: the share refunded strictly inside the 24-hour window.</summary>
    public const int WithinWindowRefundPercentage = 50;

    /// <summary>
    /// Clause 4: the provider must be more than this many minutes late, without the customer's
    /// consent, for the session to be refunded in full. Exactly 60 minutes does not trigger it.
    /// </summary>
    public const int ProviderLatenessThresholdMinutes = 60;

    /// <summary>Clause 5: quality complaints are normally reported within 48 hours of the visit.</summary>
    public static readonly TimeSpan ComplaintReportingWindow = TimeSpan.FromHours(48);

    /// <summary>Clause 2: 50% expressed as an exact piastre ratio, so no decimal is introduced.</summary>
    private const int PercentageDenominator = 100;

    public static BookingRefundDecision Decide(BookingRefundPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Validate(input);

        // Revision 2 is this policy. The booking's own sold revision is asserted by the caller
        // through the policy-version dispatch; the decision records the revision it was computed
        // under so a rescheduled booking keeps its original terms (decision #8).
        BookingRefundPolicyVersion policyVersion = BookingRefundPolicyVersion.FromPersisted(
            CurrentPolicyVersion,
            BookingRefundPolicyVersion.PolicyDocumentEffectiveOn);

        long ceiling = RefundableCeiling(input.CapturedMinorUnits, input.PreviouslyRefundedMinorUnits);

        return DecideCore(input, policyVersion, ceiling);
    }

    /// <summary>
    /// Decides an act whose reason is already known (a recorded provider cancellation, no-show,
    /// safety cancellation or force majeure). The explicit reason is validated and then used as the
    /// decision's <see cref="BookingRefundDecision.OutcomeReason"/>, and it always awards the
    /// clause 4 entitlement for the affected sessions — it can never be used to lower an award.
    /// </summary>
    public static BookingRefundDecision DecideOutcome(
        BookingRefundPolicyInput input,
        BookingRefundOutcomeReason outcomeReason)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!Enum.IsDefined(outcomeReason))
            throw new DomainException("The provider/outcome reason is unknown.");

        Validate(input);

        if (input.RecipientWasInaccessible)
            throw new DomainException(
                "An inaccessible recipient cannot be recorded as a provider or safety outcome.");

        if (input.Status != BookingStatus.Confirmed)
            throw new DomainException(
                "A provider, access, safety or force-majeure outcome is only recorded for an accepted booking.");

        BookingRefundPolicyVersion policyVersion = BookingRefundPolicyVersion.FromPersisted(
            CurrentPolicyVersion,
            BookingRefundPolicyVersion.PolicyDocumentEffectiveOn);

        long ceiling = RefundableCeiling(input.CapturedMinorUnits, input.PreviouslyRefundedMinorUnits);

        return DecideCore(input, policyVersion, ceiling, forcedOutcome: outcomeReason);
    }

    private static BookingRefundDecision DecideCore(
        BookingRefundPolicyInput input,
        BookingRefundPolicyVersion policyVersion,
        long ceiling,
        BookingRefundOutcomeReason? forcedOutcome = null)
    {
        // -------------------------------------------------------------------------------------
        // Unpaid: nothing was captured, so there is no money to move and no clause to apply.
        // -------------------------------------------------------------------------------------
        if (input.Status == BookingStatus.PendingPayment)
        {
            return Build(
                input, policyVersion, ceiling,
                BookingRefundEntitlement.NoRefundDue,
                BookingRefundDecisionReason.NothingCaptured,
                outcomeReason: null,
                awarded: 0,
                basis: null,
                deliveredNonRefundable: false,
                refunded: []);
        }

        // -------------------------------------------------------------------------------------
        // Awaiting caregiver approval: the booking was never accepted. Both sides get everything.
        // -------------------------------------------------------------------------------------
        if (input.Status == BookingStatus.PendingCaregiverApproval)
        {
            BookingRefundDecisionReason reason = input.ActorSide == BookingCancellationActorSide.Caregiver
                ? BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance
                : BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance;

            long awarded = Ceiling(ceiling, ceiling);

            return Build(
                input, policyVersion, ceiling, EntitlementFor(awarded), reason,
                outcomeReason: null, awarded, basis: null,
                deliveredNonRefundable: false, refunded: []);
        }

        // -------------------------------------------------------------------------------------
        // Confirmed. An explicitly recorded clause 4 outcome (the provider-outcome endpoints)
        // takes precedence over every timing rule and over the actor-side shortcut, because it
        // states what actually happened rather than when it was reported. Otherwise the caregiver
        // side always refunds in full, and the family side is scheduled-start relative.
        // -------------------------------------------------------------------------------------
        if (forcedOutcome is not null)
            return DecideAcceptingFamilyAct(input, policyVersion, ceiling, forcedOutcome);

        if (input.ActorSide == BookingCancellationActorSide.Caregiver)
        {
            long awarded = Ceiling(ceiling, ceiling);

            return Build(
                input, policyVersion, ceiling, EntitlementFor(awarded),
                BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance,
                outcomeReason: null, awarded, basis: null,
                deliveredNonRefundable: false, refunded: []);
        }

        return DecideAcceptingFamilyAct(input, policyVersion, ceiling, forcedOutcome);
    }

    private static BookingRefundDecision DecideAcceptingFamilyAct(
        BookingRefundPolicyInput input,
        BookingRefundPolicyVersion policyVersion,
        long ceiling,
        BookingRefundOutcomeReason? forcedOutcome)
    {
        // Clause 2, access: a recipient the provider could not reach is not a refundable failure.
        if (input.RecipientWasInaccessible)
        {
            return Build(
                input, policyVersion, ceiling,
                BookingRefundEntitlement.NoRefundDue,
                BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow,
                BookingRefundOutcomeReason.RecipientInaccessible,
                awarded: 0, basis: null, deliveredNonRefundable: false, refunded: []);
        }

        // Clause 4: an explicit outcome (provider cancellation, no-show, safety, force majeure)
        // takes precedence; otherwise a lateness reading of the same facts is used.
        BookingRefundOutcomeReason? providerOutcome = forcedOutcome ?? ResolveProviderOutcome(input);

        // Multi-session package: award the exact allocations of the refundable sessions.
        if (input.AllocationList.Count > 0)
            return DecidePackage(input, policyVersion, ceiling, providerOutcome);

        // Single booking.
        if (providerOutcome is not null)
        {
            long awarded = Ceiling(ceiling, ceiling);

            return Build(
                input, policyVersion, ceiling, EntitlementFor(awarded),
                BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow,
                providerOutcome, awarded, basis: null,
                deliveredNonRefundable: false, refunded: []);
        }

        return DecideScheduledStartShare(input, policyVersion, ceiling);
    }

    private static BookingRefundDecision DecidePackage(
        BookingRefundPolicyInput input,
        BookingRefundPolicyVersion policyVersion,
        long ceiling,
        BookingRefundOutcomeReason? providerOutcome)
    {
        IReadOnlyList<BookingSessionAllocation> allocations = input.AllocationList;

        long awardedTotal = 0;
        List<BookingSessionAllocation> refunded = [];
        bool deliveredNonRefundable = false;

        foreach (BookingSessionAllocation allocation in allocations)
        {
            if (allocation.IsDelivered)
            {
                // Clause 3: delivered sessions are non-refundable, always.
                deliveredNonRefundable = true;
                continue;
            }

            long sessionAward;

            if (allocation.Outcome == BookingSessionOutcome.ProviderFailed || providerOutcome is not null)
            {
                // Clause 4: the provider failed this session, so it refunds in full regardless of timing.
                sessionAward = allocation.AllocatedMinorUnits;
            }
            else
            {
                // Clause 2/3: unused sessions refund at their allocated discounted price, and a
                // future session cancelled at least 24 hours ahead refunds in full.
                sessionAward = SessionShare(input, allocation.AllocatedMinorUnits);
            }

            if (sessionAward > 0)
            {
                awardedTotal += sessionAward;
                refunded.Add(allocation);
            }
        }

        long awarded = Ceiling(ceiling, awardedTotal);

        // The reason records which amount was actually paid: a session-level denial is not
        // described as the family having won its timing argument.
        BookingRefundDecisionReason reason = providerOutcome is not null
            ? BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow
            : SessionTimingReason(input);

        // The basis is what a full refund of the refundable sessions would have been, so an award
        // that had to drop a delivered session is reported as a partial refund rather than as the
        // whole captured amount.
        long basis = 0;
        foreach (BookingSessionAllocation allocation in allocations)
        {
            if (!allocation.IsDelivered)
                basis += allocation.AllocatedMinorUnits;
        }

        return Build(
            input, policyVersion, ceiling, EntitlementFor(awarded, basis), reason, providerOutcome,
            awarded, basis, deliveredNonRefundable, refunded);
    }

    private static BookingRefundDecision DecideScheduledStartShare(
        BookingRefundPolicyInput input,
        BookingRefundPolicyVersion policyVersion,
        long ceiling)
    {
        long basis = ceiling;
        long awarded = SessionShare(input, basis);

        // A zero captured amount is reported as "nothing captured" rather than as a timing denial;
        // any positive award is the family's timing argument winning, whether it is the full
        // 24-hour refund or the 50% share inside the window; a zero award on real money is the
        // at-or-after-start denial.
        BookingRefundDecisionReason reason;
        if (basis == 0)
            reason = BookingRefundDecisionReason.NothingCaptured;
        else if (awarded > 0)
            reason = BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow;
        else
            reason = BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow;

        return Build(
            input, policyVersion, ceiling, EntitlementFor(awarded, basis), reason,
            outcomeReason: null, awarded, basis, deliveredNonRefundable: false, refunded: []);
    }

    /// <summary>
    /// The share of one session's allocated amount that an ordinary (non-provider-failure) act
    /// refunds: the full allocated amount at or before 24 hours before the scheduled start, 50%
    /// strictly inside the window, and nothing at or after the start.
    /// </summary>
    private static long SessionShare(BookingRefundPolicyInput input, long allocatedMinorUnits)
    {
        DateTime scheduledStart = input.ScheduledStartUtc!.Value;

        if (input.ActedOnUtc >= scheduledStart)
            return 0;

        if (input.ActedOnUtc <= scheduledStart - FullRefundWindow)
            return allocatedMinorUnits;

        return HalfToEven(allocatedMinorUnits);
    }

    private static BookingRefundDecisionReason SessionTimingReason(BookingRefundPolicyInput input)
    {
        DateTime scheduledStart = input.ScheduledStartUtc!.Value;

        if (input.ActedOnUtc >= scheduledStart)
            return BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow;

        return BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow;
    }

    private static BookingRefundOutcomeReason? ResolveProviderOutcome(BookingRefundPolicyInput input)
    {
        // Owner amendment to clause 4: force majeure refunds undelivered services and unused
        // sessions. It is resolved before lateness so a documented force-majeure event is not
        // downgraded to a lateness question the customer's consent could have waived.
        if (input.ForceMajeure)
            return BookingRefundOutcomeReason.ForceMajeure;

        if (input.ProviderLatenessMinutes is { } minutes)
        {
            if (minutes < 0)
                throw new DomainException("Provider lateness cannot be a negative number of minutes.");

            if (minutes > ProviderLatenessThresholdMinutes && !input.CustomerConsentedToLateness)
                return BookingRefundOutcomeReason.ProviderLateBeyondThreshold;

            return null;
        }

        return null;
    }

    /// <summary>
    /// Distributes a paid package total across its sessions so the allocations sum exactly to the
    /// total (Owner decision #5). The remainder in piastres is spread over the first
    /// <c>total % count</c> sessions, so the sum is exact, no piastre is lost, and no fractional
    /// amount is ever stored. A package whose total does not divide evenly therefore allocates at
    /// most one extra piastre to some sessions.
    /// </summary>
    public static IReadOnlyList<BookingSessionAllocation> AllocatePackageSessions(
        long paidPackageMinorUnits,
        int sessionCount)
    {
        if (sessionCount < BookingSessionAllocation.FirstSessionIndex)
            throw new DomainException("A package must contain at least one session.");

        if (paidPackageMinorUnits < 0)
            throw new DomainException("A package total cannot be a negative amount.");

        long perSession = paidPackageMinorUnits / sessionCount;
        long remainder = paidPackageMinorUnits % sessionCount;

        List<BookingSessionAllocation> allocations = new(sessionCount);

        for (int index = 0; index < sessionCount; index++)
        {
            long amount = perSession + (index < remainder ? 1 : 0);
            allocations.Add(BookingSessionAllocation.Create(
                index + BookingSessionAllocation.FirstSessionIndex,
                amount));
        }

        return allocations;
    }

    /// <summary>Clause 5: true when a quality complaint arrived inside the 48-hour reporting window.</summary>
    public static bool IsComplaintWithinReportingWindow(DateTime visitedOnUtc, DateTime reportedOnUtc)
    {
        ValidateTimestamp(visitedOnUtc, "Visit time");
        ValidateTimestamp(reportedOnUtc, "Complaint time");

        if (reportedOnUtc < visitedOnUtc)
            throw new DomainException("A complaint cannot be reported before the visit it concerns.");

        return reportedOnUtc - visitedOnUtc <= ComplaintReportingWindow;
    }

    /// <summary>
    /// Clause 8 "reschedule": free at or before 24 hours before the scheduled start, subject to
    /// provider availability. The availability check is a caller concern; this answers the fee.
    /// A reschedule keeps the original policy version (decision #8), so this is not a money award.
    /// </summary>
    public static bool IsRescheduleFreeOfCharge(DateTime scheduledStartUtc, DateTime requestedOnUtc)
    {
        ValidateTimestamp(scheduledStartUtc, "Scheduled start time");
        ValidateTimestamp(requestedOnUtc, "Reschedule request time");

        return requestedOnUtc <= scheduledStartUtc - FullRefundWindow;
    }

    // -----------------------------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------------------------

    private static void Validate(BookingRefundPolicyInput input)
    {
        if (!Enum.IsDefined(input.Status))
            throw new DomainException("Booking status is invalid.");

        if (!Enum.IsDefined(input.ActorSide))
            throw new DomainException("Cancellation actor side is invalid.");

        if (!Enum.IsDefined(input.Action))
            throw new DomainException("Cancellation action is invalid.");

        if (!Enum.IsDefined(input.CaptureEvidence))
            throw new DomainException("Capture evidence is invalid.");

        if (input.CapturedMinorUnits < 0)
            throw new DomainException("Captured money cannot be a negative amount.");

        if (input.PreviouslyRefundedMinorUnits < 0)
            throw new DomainException("Previously refunded money cannot be a negative amount.");

        ValidateTimestamp(input.ActedOnUtc, "Cancellation time");

        if (input.ScheduledStartUtc is not null)
            ValidateTimestamp(input.ScheduledStartUtc.Value, "Scheduled start time");

        if (input.ConfirmedOnUtc is not null)
            ValidateTimestamp(input.ConfirmedOnUtc.Value, "Acceptance time");

        if (input.StartedOnUtc is not null)
            ValidateTimestamp(input.StartedOnUtc.Value, "Visit start time");

        switch (input.Status)
        {
            case BookingStatus.InProgress:
            case BookingStatus.Completed:
                throw new DomainException(
                    "A booking whose visit has already started or completed can no longer be cancelled.");

            case BookingStatus.CancelledByFamily:
            case BookingStatus.CancelledByCaregiver:
            case BookingStatus.DeclinedByCaregiver:
            case BookingStatus.Refunded:
            case BookingStatus.Expired:
                throw new DomainException(
                    "This booking has already ended; a recorded cancellation decision is never re-evaluated.");

            case BookingStatus.PendingPayment:
            case BookingStatus.PendingCaregiverApproval:
            case BookingStatus.Confirmed:
                break;
        }

        if (input.ActorSide == BookingCancellationActorSide.Family
            && input.Action == BookingCancellationAction.Reject)
            throw new DomainException("Only a caregiver can reject a booking.");

        if (input.ActorSide == BookingCancellationActorSide.Caregiver)
        {
            if (input.Action == BookingCancellationAction.Reject
                && input.Status != BookingStatus.PendingCaregiverApproval)
                throw new DomainException(
                    "A caregiver can only reject a booking that is still awaiting approval.");

            if (input.Action == BookingCancellationAction.Cancel
                && input.Status != BookingStatus.Confirmed)
                throw new DomainException(
                    "A caregiver can only cancel a booking they have already accepted.");
        }

        ValidateAllocations(input);

        // The reason is validated here even though the decision does not carry it: a policy
        // decision must never be reached for an accepted booking that lacks the mandatory reason,
        // so the caller cannot mutate the aggregate and only then discover the reason was missing.
        if (input.Status == BookingStatus.Confirmed)
            BookingCancellationFeedback.RequireForAcceptedBooking(input.Feedback);

        if (input.RecipientWasInaccessible && input.CustomerConsentedToLateness)
            throw new DomainException(
                "An inaccessible recipient and a consented lateness are contradictory facts.");

        if (input.Status == BookingStatus.Confirmed)
        {
            if (input.ConfirmedOnUtc is null)
                throw new DomainException(
                    "An accepted booking must carry a valid acceptance time before it can be cancelled.");

            if (input.StartedOnUtc is not null)
                throw new DomainException(
                    "An accepted booking that already recorded a visit start is not cancellable.");

            if (input.ConfirmedOnUtc.Value > input.ActedOnUtc)
                throw new DomainException("Cancellation time cannot be earlier than the acceptance time.");

            if (input.ScheduledStartUtc is null)
                throw new DomainException(
                    "An accepted booking must carry its scheduled start before a refund entitlement can be decided.");
        }
        else
        {
            if (input.ConfirmedOnUtc is not null)
                throw new DomainException(
                    "A booking that was never accepted cannot carry an acceptance time.");

            if (input.StartedOnUtc is not null)
                throw new DomainException(
                    "A booking that was never accepted cannot carry a visit start time.");
        }

        ValidateCaptureEvidence(input);
    }

    private static void ValidateAllocations(BookingRefundPolicyInput input)
    {
        IReadOnlyList<BookingSessionAllocation> allocations = input.AllocationList;

        if (allocations.Count == 0)
            return;

        if (input.Status != BookingStatus.Confirmed)
            throw new DomainException(
                "Session allocations apply to an accepted booking; an unaccepted booking has no delivered or unused sessions.");

        // Session indices must be a dense 1..N range: a gap means an allocation was dropped, and
        // summing the remainder would then silently under- or over-refund the package.
        HashSet<int> indices = [];
        foreach (BookingSessionAllocation allocation in allocations)
        {
            if (!indices.Add(allocation.SessionIndex))
                throw new DomainException("A package cannot allocate the same session twice.");
        }

        for (int expected = BookingSessionAllocation.FirstSessionIndex;
            expected <= allocations.Count;
            expected++)
        {
            if (!indices.Contains(expected))
                throw new DomainException(
                    $"Session allocations must cover every session from {BookingSessionAllocation.FirstSessionIndex} to {allocations.Count} without a gap.");
        }
    }

    private static void ValidateCaptureEvidence(BookingRefundPolicyInput input)
    {
        if (input.Status == BookingStatus.PendingPayment)
        {
            if (input.CaptureEvidence != BookingCaptureEvidence.NotCaptured)
                throw new DomainException(
                    "A booking that is still awaiting payment cannot carry captured or ambiguous payment evidence.");

            return;
        }

        if (input.CaptureEvidence != BookingCaptureEvidence.Captured)
            throw new DomainException(
                "Captured payment evidence for this paid booking is missing or inconsistent; the cancellation cannot be decided until that is resolved.");
    }

    private static void ValidateTimestamp(DateTime value, string field)
    {
        if (value == default)
            throw new DomainException($"{field} is required.");

        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"{field} must be a UTC timestamp.");
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The most that may be awarded: captured money less what was already refunded, never below
    /// zero (Owner decisions #4 and #31 — a zero award is legitimate, a negative one is not).
    /// </summary>
    private static long RefundableCeiling(long capturedMinorUnits, long previouslyRefundedMinorUnits) =>
        Math.Max(0L, capturedMinorUnits - previouslyRefundedMinorUnits);

    /// <summary>Clamps an award to the decision #4 ceiling and to being non-negative.</summary>
    private static long Ceiling(long ceiling, long requestedAward) =>
        Math.Clamp(requestedAward, 0L, Math.Max(0L, ceiling));

    /// <summary>
    /// Half of an amount in exact piastres, rounding half to even. This mirrors the Finance money
    /// core's rounding (<c>Money.ScaleBy(1, 2)</c> is <c>decimal.Round(product, 0, ToEven)</c>) in
    /// integer arithmetic, so a partial award computed here cannot disagree with a proration
    /// computed through the money core.
    /// </summary>
    private static long HalfToEven(long minorUnits)
    {
        if (minorUnits <= 0)
            return 0;

        // The exact half is minorUnits / 2; its exact midpoint tie occurs only for an odd amount,
        // where the two candidates are floor(minorUnits / 2) and that value plus one.
        long lower = minorUnits / 2;

        if (minorUnits % 2 == 0)
            return lower;

        // A tie: round to the even candidate, exactly as decimal.Round(..., ToEven) would.
        return lower % 2 == 0 ? lower : lower + 1;
    }

    /// <summary>
    /// Maps an award onto the entitlement vocabulary. A zero award denies; an award that is the
    /// whole refundable basis is a full captured refund; an award that is a positive slice of it
    /// is a partial refund, so a 50% cancellation is never labelled as a full one on the wire
    /// (Owner decision #4 and clause 2).
    /// </summary>
    private static BookingRefundEntitlement EntitlementFor(long awardedMinorUnits, long? prorationBasisMinorUnits = null)
    {
        if (awardedMinorUnits <= 0)
            return BookingRefundEntitlement.NoRefundDue;

        // No basis supplied means the award was not a prorated share: an outcome award takes the
        // whole ceiling, so it is full by construction.
        if (prorationBasisMinorUnits is not { } basis || awardedMinorUnits >= basis)
            return BookingRefundEntitlement.FullCapturedRefund;

        return BookingRefundEntitlement.PartialRefund;
    }

    private static BookingRefundDecision Build(
        BookingRefundPolicyInput input,
        BookingRefundPolicyVersion policyVersion,
        long ceiling,
        BookingRefundEntitlement entitlement,
        BookingRefundDecisionReason reason,
        BookingRefundOutcomeReason? outcomeReason,
        long awarded,
        long? basis,
        bool deliveredNonRefundable,
        IReadOnlyList<BookingSessionAllocation> refunded) =>
        BookingRefundDecision.Create(
            input.Status,
            input.ActorSide,
            input.Action,
            entitlement,
            reason,
            outcomeReason,
            policyVersion,
            input.ScheduledStartUtc,
            input.ConfirmedOnUtc,
            input.ActedOnUtc,
            input.Status == BookingStatus.Confirmed,
            input.CapturedMinorUnits,
            ceiling,
            awarded,
            basis,
            deliveredNonRefundable,
            refunded);
}
