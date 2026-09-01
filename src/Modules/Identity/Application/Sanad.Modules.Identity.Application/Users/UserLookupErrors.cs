using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Users;

public static class UserLookupErrors
{
    public static readonly Error EmailNotFound =
        new(
            "Identity.User.EmailNotFound",
            "No account exists for this email address.");
}