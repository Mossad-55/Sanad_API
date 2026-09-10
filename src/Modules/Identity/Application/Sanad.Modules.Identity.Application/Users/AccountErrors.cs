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
}
