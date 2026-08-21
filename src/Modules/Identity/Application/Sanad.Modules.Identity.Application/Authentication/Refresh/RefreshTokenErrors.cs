using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Refresh;

public static class RefreshTokenErrors
{
    public static readonly Error SessionNotFound =
        new(
            "Identity.Refresh.SessionNotFound",
            "Device session was not found.");

    public static readonly Error SessionRevoked =
        new(
            "Identity.Refresh.SessionRevoked",
            "Device session is revoked.");

    public static readonly Error SessionExpired =
        new(
            "Identity.Refresh.SessionExpired",
            "Device session has expired.");

    public static readonly Error UserNotFound =
        new(
            "Identity.Refresh.UserNotFound",
            "User linked to the session was not found.");

    public static readonly Error UserNotActive =
        new(
            "Identity.Refresh.UserNotActive",
            "User is not active.");

    public static readonly Error ReuseDetected =
        new(
            "Identity.Refresh.ReuseDetected",
            "Refresh token reuse was detected. " +
            "All sessions have been revoked.");
}