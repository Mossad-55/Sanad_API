using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Users;

public static class AccountErrors
{
    public static readonly Error UnsupportedAccountType =
        new("Identity.Account.UnsupportedAccountType", "Only Family or one caregiver account type is supported.");
    public static readonly Error AccountAlreadyExists =
        new("Identity.Account.AccountAlreadyExists", "The account side already exists.");
    public static readonly Error CaregiverTypesExclusive =
        new("Identity.Account.CaregiverTypesExclusive", "Medical and Companion accounts cannot be combined.");
    public static readonly Error AccountNotOwned =
        new("Identity.Account.AccountNotOwned", "The selected account does not belong to this user.");
    public static readonly Error RetainedCaregiverProfile =
        new("Identity.Account.RetainedCaregiverProfile", "A retained caregiver profile requires support review before adding a caregiver account again.");

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

    public static readonly Error ElderlyManagedByFamily =
        new(
            "Identity.Account.ElderlyManagedByFamily",
            "Elderly accounts are managed by the family.");

    public static readonly Error OwnershipTransferRequired =
        new(
            "Identity.Account.OwnershipTransferRequired",
            "Transfer family ownership before deleting your account.");
}
