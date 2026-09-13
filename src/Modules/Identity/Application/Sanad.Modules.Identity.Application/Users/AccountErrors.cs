using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Users;

public static class AccountErrors
{
    public static readonly Error UserNotFound =
        new(
            "Identity.Account.UserNotFound",
            "User was not found.");

    public static readonly Error InvalidOperation =
        new(
            "Identity.Account.InvalidOperation",
            "The account operation is not allowed.");

    public static readonly Error CaregiverOnly =
        new(
            "Identity.Account.CaregiverOnly",
            "Only caregiver accounts can delete their account here.");

    public static readonly Error CaregiverProfileNotFound =
        new(
            "Identity.Account.CaregiverProfileNotFound",
            "The caregiver profile was not found.");

    public static readonly Error ActiveBookingExists =
        new(
            "Identity.Account.ActiveBookingExists",
            "The caregiver still has active bookings.");
}
