using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Families.Domain.Bookings;

public sealed class Booking : AggregateRoot<BookingId>
{
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
        CaregiverType caregiverType,
        BookingShiftType shiftType,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        string serviceAddress,
        string? specialInstructions,
        BookingPriceSnapshot priceSnapshot,
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
        Status = BookingStatus.PendingPayment;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FamilyId FamilyId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public CaregiverId CaregiverId { get; private set; }
    public CaregiverType CaregiverType { get; private set; }
    public BookingShiftType ShiftType { get; private set; }
    public DateOnly BookingDate { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public string ServiceAddress { get; private set; } = string.Empty;
    public string? SpecialInstructions { get; private set; }
    public BookingPriceSnapshot PriceSnapshot { get; private set; } = default!;
    public BookingStatus Status { get; private set; }
    public string? PaymobOrderId { get; private set; }
    public string? PaymobTransactionId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CaregiverNotes { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PaidOnUtc { get; private set; }
    public DateTime? ConfirmedOnUtc { get; private set; }
    public DateTime? StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public DateTime? CancelledOnUtc { get; private set; }

    public static Booking Create(
        FamilyId familyId,
        UserId createdByUserId,
        ElderlyId elderlyId,
        CaregiverId caregiverId,
        CaregiverType caregiverType,
        BookingShiftType shiftType,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        string serviceAddress,
        string? specialInstructions,
        BookingPriceSnapshot priceSnapshot,
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
    }

    public void AcceptByCaregiver(DateTime utcNow)
    {
        if (Status != BookingStatus.PendingCaregiverApproval)
            throw new DomainException("Only paid bookings awaiting approval can be accepted.");

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