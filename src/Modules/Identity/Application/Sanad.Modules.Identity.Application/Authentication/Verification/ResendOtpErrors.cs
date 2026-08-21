using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public static class ResendOtpErrors
{
    public static readonly Error RequestNotFound =
        new(
            "Identity.Verification.ResendRequestNotFound",
            "Verification request was not found.");

    public static readonly Error RequestNotPending =
        new(
            "Identity.Verification.ResendRequestNotPending",
            "Verification request is no longer pending.");

    public static readonly Error RequestSuperseded =
        new(
            "Identity.Verification.RequestSuperseded",
            "A newer verification request already exists.");

    public static readonly Error CooldownActive =
        new(
            "Identity.Verification.ResendCooldownActive",
            "Verification code cannot be resent yet.");
}