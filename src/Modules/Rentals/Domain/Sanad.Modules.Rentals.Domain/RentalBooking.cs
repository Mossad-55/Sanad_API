using System.Globalization;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Rentals.Domain;
/// <summary>Rental booking lifecycle (clause 8: booking/payment/delivery/return/inspection/deposit/refund).</summary>
public enum RentalBookingStatus
{
    /// <summary>Created against an active catalog item; payment pending.</summary>
    PendingPayment = 1,

    /// <summary>The gateway captured the prepaid rent plus deposit. Refundable in full while not delivered.</summary>
    Confirmed = 2,

    /// <summary>Cancelled before delivery. If paid, the policy refunds everything captured (clause 8).</summary>
    CancelledBeforeDelivery = 3,

    /// <summary>Physically delivered. The 24-hour period clock is anchored to this instant (#19).</summary>
    Delivered = 4,

    /// <summary>Physically returned. The seven-business-day deposit window is anchored to this instant (#20).</summary>
    Returned = 5,

    /// <summary>Deposit settled (returned, less approved on-time deductions). Terminal.</summary>
    DepositSettled = 6
}

/// <summary>
/// One equipment rental booking — the aggregate root of the clause 8
/// lifecycle. It owns the terms snapshot taken at booking time (Owner decision
/// #10: catalog/rate edits apply only to future bookings), the whole-day
/// accounting (#19: days are 24-hour periods anchored to actual delivery, a
/// started period counts as used, remaining whole prepaid periods refund), the
/// seven-business-day deposit window (#20: starts at physical return, the
/// inspection cannot postpone it) and the damage-deduction netting (#21).
/// <para>
/// Money is carried as exact EGP piastres and never recomputed from the
/// catalog. The booking never persists money by itself: every refund declares
/// its Finance refund-case id and is recorded through the Finance module seams
/// (W3 design pin items 1–3); this aggregate stores the correlation and the
/// derived amounts on its own rows and refuses amounts that would refund more
/// than was captured less previous refunds (Owner decision #4).
/// </para>
/// </summary>
public sealed class RentalBooking : AggregateRoot<Guid>
{
    public const int MaximumAddressLength = 500;
    public const int MaximumNotesLength = 2000;

    private readonly List<RentalPaymentTransaction> _paymentTransactions = [];
    private readonly List<RentalDamageDeduction> _damageDeductions = [];
    private readonly List<RentalRefundMovement> _refundMovements = [];

    private RentalBooking() { }

    private RentalBooking(
        Guid id,
        Guid equipmentId,
        Guid familyId,
        Guid requestedByUserId,
        int plannedDays,
        long dailyRateMinorUnits,
        long depositMinorUnits,
        int maximumRentalDays,
        string nameEnglish,
        string nameArabic,
        string? deliveryAddress,
        DateTime createdOnUtc) : base(id)
    {
        EquipmentId = equipmentId;
        FamilyId = familyId;
        RequestedByUserId = requestedByUserId;
        PlannedDays = plannedDays;
        DailyRateMinorUnits = dailyRateMinorUnits;
        DepositMinorUnits = depositMinorUnits;
        MaximumRentalDaysSnapshot = maximumRentalDays;
        NameEnglishSnapshot = nameEnglish;
        NameArabicSnapshot = nameArabic;
        DeliveryAddress = deliveryAddress;
        Status = RentalBookingStatus.PendingPayment;
        TermsSnapshotOnUtc = createdOnUtc;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public Guid EquipmentId { get; private set; }

    public Guid FamilyId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    /// <summary>Prepaid 24-hour periods booked. Never remapped after creation.</summary>
    public int PlannedDays { get; private set; }

    /// <summary>Snapshotted rate per 24-hour period, EGP piastres (clause 8 display-before-purchase, #10, #19).</summary>
    public long DailyRateMinorUnits { get; private set; }

    /// <summary>Snapshotted deposit, EGP piastres.</summary>
    public long DepositMinorUnits { get; private set; }

    /// <summary>Snapshotted maximum rental length at booking time.</summary>
    public int MaximumRentalDaysSnapshot { get; private set; }

    public string NameEnglishSnapshot { get; private set; } = string.Empty;

    public string NameArabicSnapshot { get; private set; } = string.Empty;

    public string? DeliveryAddress { get; private set; }

    public RentalBookingStatus Status { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (the CreditBalance/PlatformControlState
    /// pattern): two concurrent refund claims cannot silently take the same
    /// booking — the loser surfaces as a retryable conflict instead of a
    /// second money movement.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>The refund movement kind currently being executed, while any money is in flight. Cleared when the outcome row lands.</summary>
    public RentalRefundMovementKind? PendingRefundKind { get; private set; }

    /// <summary>The destination the persistently claimed refund executes toward (the claim's authoritative choice, M5/M3 binding).</summary>
    public RentalRefundDestination? PendingRefundDestination { get; private set; }

    /// <summary>The Finance refund-case correlation id claimed before any money moved (design pin 3: the correlation id lives on the owner's own rows first).</summary>
    public Guid? PendingRefundCaseId { get; private set; }

    /// <summary>The award amount claimed for the pending refund, EGP piastres.</summary>
    public long? PendingRefundAmountMinorUnits { get; private set; }

    public DateTime TermsSnapshotOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>Total captured when confirmed: prepaid rent plus deposit, EGP piastres. The #4 refund ceiling.</summary>
    public long? CapturedMinorUnits { get; private set; }

    public DateTime? DeliveredOnUtc { get; private set; }

    public DateTime? ReturnedOnUtc { get; private set; }

    /// <summary>The #20 deposit-window deadline: seven business days after physical return, Africa/Cairo calendar.</summary>
    public DateTime? SettlementDeadlineUtc { get; private set; }

    public DateTime? InspectionCompletedOnUtc { get; private set; }

    public Guid? InspectionCompletedByUserId { get; private set; }

    public string? InspectionNotes { get; private set; }

    public DateTime? CancelledOnUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTime? DepositSettledOnUtc { get; private set; }

    public Guid? DepositSettledByUserId { get; private set; }

    public IReadOnlyCollection<RentalPaymentTransaction> PaymentTransactions => _paymentTransactions.AsReadOnly();

    public IReadOnlyCollection<RentalDamageDeduction> DamageDeductions => _damageDeductions.AsReadOnly();

    public IReadOnlyCollection<RentalRefundMovement> RefundMovements => _refundMovements.AsReadOnly();

    /// <summary>Total prepaid rent at the snapshotted rate, EGP piastres.</summary>
    public long PrepaidRentMinorUnits => checked(PlannedDays * DailyRateMinorUnits);

    /// <summary>The whole amount a booking before delivery must be able to refund: captured rent plus deposit.</summary>
    public long CapturedTotalMinorUnits => CapturedMinorUnits ?? 0;

    public static RentalBooking Create(
        RentalEquipment equipment,
        Guid familyId,
        Guid requestedByUserId,
        int plannedDays,
        string? deliveryAddress,
        DateTime createdOnUtc)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        if (familyId == Guid.Empty)
            throw new DomainException("A rental booking requires a family.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("A rental booking requires the requesting user.");
        if (!equipment.IsActive)
            throw new DomainException("An inactive equipment item cannot be booked.");
        if (plannedDays is < 1 || plannedDays > equipment.MaximumRentalDays)
            throw new DomainException($"The rental length must be between 1 and {equipment.MaximumRentalDays} days.");
        string? address = string.IsNullOrWhiteSpace(deliveryAddress) ? null : deliveryAddress!.Trim();
        if (address is { Length: > MaximumAddressLength })
            throw new DomainException($"The delivery address must not exceed {MaximumAddressLength} characters.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A rental booking timestamp must be UTC.");

        // The terms snapshot (#10, #19: displayed terms are the booked terms).
        return new(
            Guid.CreateVersion7(),
            equipment.Id,
            familyId,
            requestedByUserId,
            plannedDays,
            equipment.DailyRateMinorUnits,
            equipment.DepositMinorUnits,
            equipment.MaximumRentalDays,
            equipment.NameEnglish,
            equipment.NameArabic,
            address,
            createdOnUtc);
    }

    public RentalPaymentTransaction OpenPaymentTransaction(
        string paymobOrderId,
        RentalPaymentMethod method,
        DateTime createdOnUtc)
    {
        if (Status != RentalBookingStatus.PendingPayment)
            throw new DomainException("Only a booking awaiting payment can start a payment.");

        // The intent total is the whole captured refund ceiling: prepaid rent
        // plus deposit (clause 8; the deposit is only ever returned, never
        // charged again).
        long total = checked(PrepaidRentMinorUnits + DepositMinorUnits);
        RentalPaymentTransaction transaction = RentalPaymentTransaction.Create(Id, paymobOrderId, method, total, createdOnUtc);
        _paymentTransactions.Add(transaction);
        // Mutates the aggregate (adds a child intent behind the PendingPayment
        // guard): the booking's optimistic-concurrency token serializes concurrent
        // intent creation, so two racing gateways cannot both open an intent.
        Version++;
        Touch(createdOnUtc);
        return transaction;
    }

    /// <summary>
    /// Confirms the booking against a succeeded gateway transaction. A late
    /// duplicate confirmation is an idempotent no-op, never a second capture.
    /// </summary>
    public void ConfirmPayment(RentalPaymentTransaction transaction, long paymobTransactionId, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.RentalBookingId != Id)
            throw new DomainException("The payment transaction does not belong to this booking.");
        if (paymobTransactionId <= 0)
            throw new DomainException("The settled payment requires a gateway transaction reference.");

        if (Status == RentalBookingStatus.Confirmed)
        {
            // Idempotent replay of the confirmed state — the caller re-derives
            // the outcome; nothing is captured twice (the #17 replay discipline
            // applied on the payment side).
            return;
        }

        if (Status != RentalBookingStatus.PendingPayment)
            throw new DomainException("Only a booking awaiting payment can be confirmed.");

        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("A rental booking timestamp must be UTC.");

        transaction.MarkSucceeded(paymobTransactionId.ToString(CultureInfo.InvariantCulture), utcNow);
        CapturedMinorUnits = transaction.AmountMinorUnits;
        Status = RentalBookingStatus.Confirmed;
        Version++;
        Touch(utcNow);
    }

    /// <summary>
    /// Family cancellation before delivery (clause 8: full refund). Returns
    /// <c>true</c> when the booking still awaited payment (nothing captured,
    /// nothing to refund) and <c>false</c> when the caller must execute the
    /// full captured refund. Throws when the lifecycle no longer allows
    /// cancellation.
    /// </summary>
    public bool CancelBeforeDelivery(Guid actorUserId, DateTime utcNow)
    {
        if (actorUserId == Guid.Empty)
            throw new DomainException("A cancellation requires the acting user.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("A cancellation timestamp must be UTC.");

        switch (Status)
        {
            case RentalBookingStatus.PendingPayment:
                Status = RentalBookingStatus.CancelledBeforeDelivery;
                CancelledOnUtc = utcNow;
                CancelledByUserId = actorUserId;
                Version++;
                Touch(utcNow);
                return true; // nothing was captured — no refund to execute
            case RentalBookingStatus.Confirmed:
                Status = RentalBookingStatus.CancelledBeforeDelivery;
                CancelledOnUtc = utcNow;
                CancelledByUserId = actorUserId;
                Version++;
                Touch(utcNow);
                return CapturedMinorUnits is null or 0;
            default:
                throw new DomainException("Only a booking that has not been delivered can be cancelled.");
        }
    }

    /// <summary>Ops confirmation of physical delivery. The 24-hour period clock starts here (#19).</summary>
    public void RecordDelivery(DateTime deliveredOnUtc, Guid actorUserId)
    {
        if (Status != RentalBookingStatus.Confirmed)
            throw new DomainException("Only a confirmed booking can be recorded as delivered.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("A delivery record requires the acting user.");
        ValidateUtc(deliveredOnUtc, "delivery");

        Status = RentalBookingStatus.Delivered;
        DeliveredOnUtc = deliveredOnUtc;
        Version++;
        Touch(deliveredOnUtc);
    }

    /// <summary>
    /// Ops confirmation of physical return. The seven-business-day deposit
    /// window (Owner decision #20) starts here; the deadline instant is
    /// computed by the application layer with the shared Finance business-day
    /// calendar and supplied by the caller — the aggregate only guards that it
    /// is a strictly-later UTC instant.
    /// </summary>
    public void RecordReturn(DateTime returnedOnUtc, DateTime settlementDeadlineUtc, Guid actorUserId)
    {
        if (Status != RentalBookingStatus.Delivered)
            throw new DomainException("Only a delivered booking can be recorded as returned.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("A return record requires the acting user.");
        ValidateUtc(returnedOnUtc, "return");
        ValidateUtc(settlementDeadlineUtc, "settlement deadline");
        if (settlementDeadlineUtc <= returnedOnUtc)
            throw new DomainException("The deposit settlement deadline must be after the return.");

        Status = RentalBookingStatus.Returned;
        ReturnedOnUtc = returnedOnUtc;
        SettlementDeadlineUtc = settlementDeadlineUtc;
        Version++;
        Touch(returnedOnUtc);
    }

    /// <summary>Records the inspection completion (clause 8 "receipt and inspection"; #20, #21).</summary>
    public void CompleteInspection(DateTime inspectedOnUtc, Guid actorUserId, string? notes)
    {
        if (Status != RentalBookingStatus.Returned)
            throw new DomainException("Only a returned booking can be inspected.");
        if (InspectionCompletedOnUtc is not null)
            throw new DomainException("The inspection was already completed.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("An inspection record requires the acting user.");
        ValidateUtc(inspectedOnUtc, "inspection");

        string? trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim();
        if (trimmed is { Length: > MaximumNotesLength })
            throw new DomainException($"The inspection notes must not exceed {MaximumNotesLength} characters.");

        InspectionCompletedOnUtc = inspectedOnUtc;
        InspectionCompletedByUserId = actorUserId;
        InspectionNotes = trimmed;
        Version++;
        Touch(inspectedOnUtc);
    }

    /// <summary>Records one documented damage deduction found at inspection (#21). Only while the deposit is unsettled.</summary>
    public RentalDamageDeduction AddDamageDeduction(
        string descriptionEnglish,
        string? descriptionArabic,
        long amountMinorUnits,
        string evidenceReference,
        Guid recordedByAdminId,
        DateTime recordedOnUtc)
    {
        if (Status != RentalBookingStatus.Returned)
            throw new DomainException("Damage deductions are recorded only after the item was returned.");

        RentalDamageDeduction deduction = RentalDamageDeduction.Create(
            Id, descriptionEnglish, descriptionArabic, amountMinorUnits, evidenceReference, recordedByAdminId, recordedOnUtc);
        _damageDeductions.Add(deduction);
        Version++;
        Touch(recordedOnUtc);
        return deduction;
    }

    /// <summary>
    /// The deposit settlement award, EGP piastres: the snapshotted deposit less
    /// the deductions that actually net, floored at zero (Owner decision #31
    /// floor semantics — excess deductions never invert the award). A deduction
    /// nets only when it is approved AND its approval is on or before the
    /// settlement deadline (#20 — past the deadline the deposit can no longer
    /// be held, so a late approval is recorded evidence that never nets).
    /// <para>
    /// <paramref name="settleOnUtc"/> after the deadline means the hold window
    /// expired: the deposit is released in full and no deduction nets, whatever
    /// its approval time — the overdue damage claim remains recorded evidence.
    /// </para>
    /// </summary>
    public long ComputeDepositSettlementAmount(DateTime settleOnUtc)
    {
        if (Status != RentalBookingStatus.Returned)
            throw new DomainException("Only a returned booking can settle its deposit.");
        ValidateUtc(settleOnUtc, "settlement");
        DateTime deadline = SettlementDeadlineUtc
            ?? throw new DomainException("The deposit settlement deadline is not anchored.");

        bool holdWindowOpen = settleOnUtc <= deadline;

        long netted = 0;
        foreach (RentalDamageDeduction deduction in _damageDeductions)
        {
            if (holdWindowOpen && deduction.NetsDeposit(deadline))
                netted = checked(netted + deduction.AmountMinorUnits);
        }

        // Floor at zero (#31: no negative award), and the net can never exceed
        // the deposit itself — deductions beyond the deposit still release the
        // full deposit refund and the excess stays a documented claim.
        return DepositMinorUnits <= netted ? 0 : DepositMinorUnits - netted;
    }

    /// <summary>
    /// The early-return award, EGP piastres: the unused whole prepaid periods
    /// at the snapshotted rate (clause 8; #19: a started period counts as used,
    /// the first period starts at actual delivery). Zero once the prepaid
    /// horizon has passed — a late return owes nothing and is never overpaid.
    /// <para>
    /// Allowed while the booking stands Returned <em>or</em> DepositSettled:
    /// the deposit settlement and the unused-days refund are exactly-once
    /// movements of different kinds whose order must not matter — settling the
    /// deposit first never forecloses the unused-days refund (clause 8 keeps
    /// the two flows apart, and #4's cumulative ceiling still bounds the sum).
    /// </para>
    /// </summary>
    public long ComputeEarlyReturnUnusedDaysAmount()
    {
        if (Status is not (RentalBookingStatus.Returned or RentalBookingStatus.DepositSettled)
            || DeliveredOnUtc is null || ReturnedOnUtc is null)
            throw new DomainException("The unused-days refund needs the recorded delivery and return.");

        TimeSpan elapsed = ReturnedOnUtc.Value - DeliveredOnUtc.Value;
        // A return at the exact delivery instant has no measurable rental time,
        // so every whole prepaid period is unused. Any started period counts as
        // used (#19): 0 < elapsed <= 24h is one used period, 24h < elapsed <=
        // 48h is two, and so on. The ceiling is therefore applied only when
        // time actually elapsed, and never exceeds the prepaid days.
        long usedPeriods = elapsed.Ticks == 0
            ? 0
            : (long)Math.Ceiling(elapsed.Ticks / (double)TimeSpan.FromHours(24).Ticks);
        if (usedPeriods > PlannedDays)
            usedPeriods = PlannedDays;

        long unusedPeriods = PlannedDays - usedPeriods;
        return checked(unusedPeriods * DailyRateMinorUnits);
    }

    /// <summary>
    /// Claims the refund-case correlation for one movement kind BEFORE any
    /// money moves, so a crash between the Finance seam and the local
    /// SaveChanges can always be replayed with the same case id (the case-level
    /// ledger replay then detects the finished movement instead of repeating
    /// it — the #17 discipline). Refuses when a movement of the kind is already
    /// recorded or another refund claim is in flight; validates the claim
    /// amount against the kind's derived award.
    /// </summary>
    public void BeginRefund(
        RentalRefundMovementKind kind,
        long amountMinorUnits,
        Guid refundCaseId,
        RentalRefundDestination destination,
        DateTime utcNow)
    {
        if (refundCaseId == Guid.Empty)
            throw new DomainException("A refund claim requires a Finance refund case id.");
        if (amountMinorUnits < 0)
            throw new DomainException("A refund award cannot be negative.");
        if (!Enum.IsDefined(destination) ||
            destination is not (RentalRefundDestination.OriginalPaymentMethod or RentalRefundDestination.AppCredit))
            throw new DomainException("This refund destination is not available in this stage.");
        ValidateUtc(utcNow, "refund claim");
        if (_refundMovements.Exists(movement => movement.Kind == kind))
            throw new DomainException("This refund movement was already recorded for the booking.");
        if (PendingRefundKind is not null)
            throw new DomainException("Another refund claim is already in flight for this booking.");

        long derived = kind switch
        {
            RentalRefundMovementKind.CancellationFullRefund => CapturedTotalMinorUnits,
            RentalRefundMovementKind.EarlyReturnUnusedDays => ComputeEarlyReturnUnusedDaysAmount(),
            RentalRefundMovementKind.DepositSettlement => ComputeDepositSettlementAmount(utcNow),
            _ => throw new DomainException("A refund claim requires a known movement kind.")
        };

        if (derived != amountMinorUnits)
            throw new DomainException("The refund award does not match the booking's policy-derived amount.");

        Version++;
        PendingRefundKind = kind;
        PendingRefundCaseId = refundCaseId;
        PendingRefundAmountMinorUnits = amountMinorUnits;
        PendingRefundDestination = destination;
        Touch(utcNow);
    }

    /// <summary>
    /// Lands the movement outcome row for the pending refund claim and clears
    /// the claim. The <see cref="RefundMovements"/> unique (booking, kind)
    /// boundary is the idempotency race guard — appending a second movement of
    /// the same kind is refused; the caller reads and replays the existing row
    /// instead. The awarded amount plus every previous recorded award of this
    /// booking can never exceed the captured total (Owner decision #4).
    /// </summary>
    public RentalRefundMovement CompleteRefund(
        RentalRefundDestination destination,
        Guid actorUserId,
        string? gatewayReference,
        string? evidenceReference,
        DateTime recordedOnUtc)
    {
        if (PendingRefundKind is not RentalRefundMovementKind kind)
            throw new DomainException("No refund claim is in flight for this booking.");
        if (PendingRefundCaseId is not Guid refundCaseId || PendingRefundAmountMinorUnits is not long amount)
            throw new DomainException("The pending refund claim is incomplete.");
        // M3 destination binding: the claim's persisted destination is authoritative —
        // a completion under a different destination never lands (the claim survives).
        if (PendingRefundDestination is RentalRefundDestination claimedDestination && destination != claimedDestination)
            throw new DomainException("The refund claim was taken toward a different destination.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("A refund outcome requires the acting user.");
        ValidateUtc(recordedOnUtc, "refund outcome");

        if (_refundMovements.Any(movement => movement.Kind == kind))
            throw new DomainException("This refund movement was already recorded for the booking.");

        long previouslyRefunded = 0;
        foreach (RentalRefundMovement existingMovement in _refundMovements)
            previouslyRefunded = checked(previouslyRefunded + existingMovement.AmountMinorUnits);

        if (previouslyRefunded + amount > CapturedTotalMinorUnits)
            throw new DomainException("The refund award would exceed the captured funds less previous refunds.");

        RentalRefundMovement movement = RentalRefundMovement.Create(
            Id, kind, refundCaseId, destination, amount, actorUserId, gatewayReference, evidenceReference, recordedOnUtc);
        _refundMovements.Add(movement);

        Version++;
        PendingRefundKind = null;
        PendingRefundCaseId = null;
        PendingRefundAmountMinorUnits = null;
        PendingRefundDestination = null;
        Touch(recordedOnUtc);
        return movement;
    }

    /// <summary>
    /// Releases an in-flight refund claim after a VERIFIED non-movement outcome
    /// (a definitive provider refusal that was signed off on the refund ledger —
    /// never an ambiguous outcome, which stays claimed and blocked for operations
    /// reconciliation, Owner decision #17). The claim columns clear so a safe
    /// retry can start a fresh refund case; no movement row is recorded because
    /// no money moved.
    /// </summary>
    public void ReleaseRefundClaim(DateTime utcNow)
    {
        if (PendingRefundKind is null)
            throw new DomainException("No refund claim is in flight for this booking.");
        ValidateUtc(utcNow, "refund claim release");

        Version++;
        PendingRefundKind = null;
        PendingRefundCaseId = null;
        PendingRefundAmountMinorUnits = null;
        PendingRefundDestination = null;
        Touch(utcNow);
    }

    /// <summary>
    /// SuperAdmin approval of a documented damage deduction (#21), owned by the
    /// booking aggregate so the approval races with settlement and refund claims
    /// on the booking's optimistic-concurrency token instead of racing silently
    /// on a child row. Rules: the booking must still stand Returned (an already
    /// settled deposit refuses later approvals — nothing is left to net), and a
    /// past-deadline approval is still recorded evidence that never nets (#20:
    /// the deadline rule is applied at settlement computation).
    /// </summary>
    public void ApproveDamageDeduction(Guid deductionId, Guid adminId, DateTime approvedOnUtc)
    {
        if (Status != RentalBookingStatus.Returned)
            throw new DomainException("Damage deductions can only be approved while the deposit is unsettled.");
        ValidateUtc(approvedOnUtc, "deduction approval");

        RentalDamageDeduction deduction = _damageDeductions.FirstOrDefault(candidate => candidate.Id == deductionId)
            ?? throw new DomainException("The damage deduction does not belong to this booking.");

        deduction.Approve(adminId, approvedOnUtc);
        Version++;
        Touch(approvedOnUtc);
    }

    /// <summary>
    /// The family's dispute of a deduction (#21), routed through the booking
    /// aggregate so the dispute records on the booking's concurrency token.
    /// </summary>
    public void RecordFamilyDamageDispute(Guid deductionId, Guid requestedByUserId, DateTime disputedOnUtc, string? reason)
    {
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("A deduction dispute requires the requesting user.");
        ValidateUtc(disputedOnUtc, "deduction dispute");

        RentalDamageDeduction deduction = _damageDeductions.FirstOrDefault(candidate => candidate.Id == deductionId)
            ?? throw new DomainException("The damage deduction does not belong to this booking.");

        deduction.RecordDispute(disputedOnUtc, reason);
        Version++;
        Touch(disputedOnUtc);
    }

    /// <summary>
    /// Staff resolution of a disputed deduction (#21), routed through the booking
    /// aggregate for the same concurrency protection. The dispute itself is never
    /// rewritten; resolution is recorded evidence.
    /// </summary>
    public void ResolveDamageDispute(Guid deductionId, Guid adminId, DateTime resolvedOnUtc)
    {
        ValidateUtc(resolvedOnUtc, "dispute resolution");

        RentalDamageDeduction deduction = _damageDeductions.FirstOrDefault(candidate => candidate.Id == deductionId)
            ?? throw new DomainException("The damage deduction does not belong to this booking.");

        deduction.ResolveDispute(adminId, resolvedOnUtc);
        Version++;
        Touch(resolvedOnUtc);
    }

    /// <summary>Terminal deposit settlement. Called after the settlement movement landed through the Finance seams.</summary>
    public void MarkDepositSettled(DateTime settledOnUtc, Guid actorUserId)
    {
        if (Status != RentalBookingStatus.Returned)
            throw new DomainException("Only a returned booking can settle its deposit.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("A deposit settlement requires the acting user.");
        ValidateUtc(settledOnUtc, "settlement");

        Status = RentalBookingStatus.DepositSettled;
        DepositSettledOnUtc = settledOnUtc;
        DepositSettledByUserId = actorUserId;
        Version++;
        Touch(settledOnUtc);
    }

    /// <summary>True when this refund movement kind already has its recorded outcome row.</summary>
    public bool HasRefundMovement(RentalRefundMovementKind kind) =>
        _refundMovements.Exists(movement => movement.Kind == kind);

    /// <summary>The recorded outcome row of a refund movement kind, or null.</summary>
    public RentalRefundMovement? RefundMovementOf(RentalRefundMovementKind kind) =>
        _refundMovements.FirstOrDefault(movement => movement.Kind == kind);

    /// <summary>The single succeeded payment transaction, or null while payment is pending.</summary>
    public RentalPaymentTransaction? SucceededPaymentTransaction() =>
        _paymentTransactions.FirstOrDefault(transaction => transaction.Status == RentalPaymentTransactionStatus.Succeeded);

    /// <summary>The currently pending payment transaction, or null.</summary>
    public RentalPaymentTransaction? PendingPaymentTransaction() =>
        _paymentTransactions.LastOrDefault(transaction => transaction.Status == RentalPaymentTransactionStatus.Pending);

    private void Touch(DateTime utcNow)
    {
        UpdatedOnUtc = utcNow;
    }

    private static void ValidateUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"The rental booking {field} timestamp must be UTC.");
    }
}
