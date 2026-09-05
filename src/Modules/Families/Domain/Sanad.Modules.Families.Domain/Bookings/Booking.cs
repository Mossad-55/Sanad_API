using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;

namespace Sanad.Modules.Families.Domain.Bookings;

public sealed class Booking : AggregateRoot<BookingId>
{
    private readonly List<PaymentTransaction> _paymentTransactions = [];

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
        DateTime createdOnUtc)
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
        AcceptanceDeadlineUtc = acceptanceDeadlineUtc;
        Status = BookingStatus.PendingPayment;

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
    public BookingPriceSnapshot PriceSnapshot { get; private set; } = default!;
    public DateTime AcceptanceDeadlineUtc { get; private set; }
    public BookingStatus Status { get; private set; }
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
        DateTime createdOnUtc)
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
            createdOnUtc);
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

        Status = BookingStatus.InProgress;
        StartedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void CompleteVisit(string? caregiverNotes, DateTime utcNow)
    {
        if (Status != BookingStatus.InProgress)
            throw new DomainException("Only InProgress bookings can be completed.");

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