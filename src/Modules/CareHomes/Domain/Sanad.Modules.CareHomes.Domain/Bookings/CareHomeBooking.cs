using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

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
        TimeSpan checkoutHoldDuration)
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
    public CareHomeRefundStatus RefundStatus { get; private set; }
    public decimal? RefundAmount { get; private set; }
    public string? RefundFailureReason { get; private set; }
    public DateTime? RefundCompletedOnUtc { get; private set; }
    public UserId? RefundCompletedBy { get; private set; }
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
        TimeSpan? checkoutHoldDuration = null)
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
            relationship, careNotes, utcNow, checkoutHold)
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
        TimeSpan? checkoutHoldDuration = null)
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
            currentSegment.CareNeedsNotes, utcNow, checkoutHold)
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

        decimal amount = ActualCheckInOnUtc is null ? TotalAmount : decimal.Round(TotalAmount / 2m, 2, MidpointRounding.AwayFromZero);
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
    {
        if (ExtensionRootBookingId is null || StartDate <= stayEndedOn || ActualCheckInOnUtc is not null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidExtensionState");
        if (Status is not (CareHomeBookingStatus.PendingPayment or CareHomeBookingStatus.PaidAwaitingDecision or CareHomeBookingStatus.Accepted))
            return;

        if (PaymentStatus == CareHomeBookingPaymentStatus.Paid)
            BeginRefund("The existing stay ended before this extension period began.", TotalAmount, utcNow);
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

    public void ExpireIfNeeded(DateTime utcNow)
    {
        if (Status == CareHomeBookingStatus.PendingPayment && CheckoutHoldUntilUtc <= utcNow) { Status = CareHomeBookingStatus.Expired; Touch(utcNow); }
        else if (Status == CareHomeBookingStatus.PaidAwaitingDecision && DecisionHoldUntilUtc <= utcNow) { BeginRefund("Facility decision window expired.", TotalAmount, utcNow); }
    }

    public bool TryClaimRefund(DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.RefundPending
            || RefundStatus is not (CareHomeRefundStatus.Pending or CareHomeRefundStatus.Failed)
            || RefundAmount is null
            || RefundClaimedOnUtc is not null) return false;
        RefundFailureReason = null;
        RefundClaimedOnUtc = utcNow; Touch(utcNow); return true;
    }

    public void MarkRefundFailed(string reason, DateTime utcNow)
    {
        if (Status != CareHomeBookingStatus.RefundPending || RefundClaimedOnUtc is null)
            throw new InvalidOperationException("CareHomes.Bookings.InvalidState");
        RefundStatus = CareHomeRefundStatus.Failed;
        RefundFailureReason = string.IsNullOrWhiteSpace(reason) ? "Paymob refund failed." : reason.Trim();
        RefundClaimedOnUtc = null;
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

    public bool MarkRefundCompletedFromProviderCallback(string? refundReference, long? parentTransactionId, DateTime utcNow)
    {
        if (RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted
            || RefundStatus != CareHomeRefundStatus.Initiated
            || string.IsNullOrWhiteSpace(RefundReference)) return false;
        bool matchesRefund = string.Equals(RefundReference, refundReference, StringComparison.Ordinal);
        bool matchesPayment = PaymobTransactionId.HasValue && PaymobTransactionId.Value == parentTransactionId;
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
