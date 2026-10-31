using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Identity.Application.Users;

public static class UserLookupErrors
{
    public static readonly Error EmailNotFound =
        new(
            "Identity.User.EmailNotFound",
            "No account exists for this email address.");
}