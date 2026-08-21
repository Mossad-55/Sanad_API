using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public static class SessionManagementErrors
{
    public static readonly Error SessionNotFound =
        new(
            "Identity.Sessions.SessionNotFound",
            "Device session was not found.");

    public static readonly Error SessionNotOwned =
        new(
            "Identity.Sessions.SessionNotOwned",
            "Device session does not belong to the current user.");

    public static readonly Error UserNotFound =
        new(
            "Identity.Sessions.UserNotFound",
            "User was not found.");
}