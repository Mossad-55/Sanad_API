using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public static class VerifyOtpErrors
{
    public static readonly Error RequestNotFound =
        new(
            "Identity.Verification.RequestNotFound",
            "Verification request was not found.");

    public static readonly Error RequestNotPending =
        new(
            "Identity.Verification.RequestNotPending",
            "Verification request is no longer pending.");

    public static readonly Error RequestExpired =
        new(
            "Identity.Verification.RequestExpired",
            "Verification request has expired.");

    public static readonly Error InvalidCode =
        new(
            "Identity.Verification.InvalidCode",
            "Verification code is invalid.");

    public static readonly Error UnsupportedPurpose =
        new(
            "Identity.Verification.UnsupportedPurpose",
            "This verification flow supports Email " +
            "and Phone verification only.");

    public static readonly Error UserNotFound =
        new(
            "Identity.Verification.UserNotFound",
            "The User linked to this request was not found.");
}