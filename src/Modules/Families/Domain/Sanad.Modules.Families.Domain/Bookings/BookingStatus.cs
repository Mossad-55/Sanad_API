namespace Sanad.Modules.Families.Domain.Bookings;

public enum BookingStatus
{
    PendingPayment = 1,
    PendingCaregiverApproval = 2,
    Confirmed = 3,
    InProgress = 4,
    Completed = 5,
    CancelledByFamily = 6,
    DeclinedByCaregiver = 7,
    CancelledByCaregiver = 8,
    Refunded = 9
}