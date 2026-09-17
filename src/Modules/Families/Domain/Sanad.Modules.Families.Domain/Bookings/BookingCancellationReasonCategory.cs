namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Reason category supplied with the cancellation of an accepted booking.
/// Values start at 1 on purpose: the default (0) is not a defined category, so an unmapped or
/// client-fabricated value is rejected instead of being read as a category.
/// </summary>
public enum BookingCancellationReasonCategory
{
    Emergency = 1,
    MedicalIssues = 2,
    TransportationIssues = 3,
    AccountDeletion = 4,
    Other = 5
}
