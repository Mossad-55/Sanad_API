using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.API.ProblemDetail;

public static class ResultProblemDetailsMapper
{
    private static readonly IReadOnlyDictionary<
        string,
        int> StatusCodesByErrorCode =
        new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["Identity.ElderlyLogin.OtpVerificationFailed"] = 401,
            ["Identity.ElderlyLogin.SessionLimitReached"] = 409,

            ["Identity.Login.InvalidCredentials"] = 401,
            ["Identity.Login.UserSuspended"] = 403,
            ["Identity.Login.UserBlocked"] = 403,
            ["Identity.Login.SessionLimitReached"] = 409,

            ["Identity.Password.UserNotFound"] = 401,
            ["Identity.Password.UserNotActive"] = 403,
            ["Identity.Password.UserHasNoPassword"] = 400,
            ["Identity.Password.InvalidCurrentPassword"] = 401,
            ["Identity.Password.OtpVerificationFailed"] = 401,
            ["Identity.Password.PendingRequestNotFound"] = 401,
            ["Identity.Password.NewPasswordMustDiffer"] = 400,

            ["Identity.Refresh.SessionNotFound"] = 401,
            ["Identity.Refresh.SessionRevoked"] = 401,
            ["Identity.Refresh.SessionExpired"] = 401,
            ["Identity.Refresh.UserNotFound"] = 401,
            ["Identity.Refresh.UserNotActive"] = 403,
            ["Identity.Refresh.ReuseDetected"] = 401,

            ["Identity.Registration.EmailAlreadyInUse"] = 409,
            ["Identity.Registration.PhoneAlreadyInUse"] = 409,
            ["Identity.Registration.UnsupportedAccountType"] = 400,

            ["Identity.Sessions.SessionNotFound"] = 404,
            ["Identity.Sessions.SessionNotOwned"] = 404,
            ["Identity.Sessions.UserNotFound"] = 404,

            ["Identity.Verification.ResendRequestNotFound"] = 404,
            ["Identity.Verification.ResendRequestNotPending"] = 400,
            ["Identity.Verification.RequestSuperseded"] = 409,
            ["Identity.Verification.ResendCooldownActive"] = 409,
            ["Identity.Verification.RequestNotFound"] = 401,
            ["Identity.Verification.RequestNotPending"] = 400,
            ["Identity.Verification.RequestExpired"] = 401,
            ["Identity.Verification.InvalidCode"] = 401,
            ["Identity.Verification.UnsupportedPurpose"] = 400,
            ["Identity.Verification.UserNotFound"] = 401,

            ["Cms.Splash.InternalNameAlreadyInUse"] = 409,
            ["Cms.Splash.NotFound"] = 404,

            ["Caregivers.Lookups.NameAlreadyInUse"] = 409,
            ["Caregivers.Lookups.NotFound"] = 404,

            ["Storage.File.Empty"] = 400,
            ["Storage.File.TooLarge"] = 400,
            ["Storage.File.UnsupportedType"] = 400,
        };

    public static ProblemDetails Create(
        Error error,
        HttpContext httpContext)
    {
        int statusCode =
            StatusCodesByErrorCode.TryGetValue(
                error.Code,
                out int mappedStatusCode)
                ? mappedStatusCode
                : StatusCodes.Status400BadRequest;

        var problemDetails =
            new ProblemDetails
            {
                Type =
                    $"https://httpstatuses.com/{statusCode}",
                Title = GetTitle(statusCode),
                Status = statusCode,
                Detail = GetSafeDetail(statusCode),
                Instance = httpContext.Request.Path
            };

        problemDetails.Extensions["code"] =
            error.Code;

        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        return problemDetails;
    }

    private static string GetTitle(
        int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest =>
                "Bad Request",

            StatusCodes.Status401Unauthorized =>
                "Unauthorized",

            StatusCodes.Status403Forbidden =>
                "Forbidden",

            StatusCodes.Status404NotFound =>
                "Not Found",

            StatusCodes.Status409Conflict =>
                "Conflict",

            _ =>
                "Internal Server Error"
        };
    }

    private static string GetSafeDetail(
        int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized =>
                "Authentication failed.",

            StatusCodes.Status403Forbidden =>
                "The requested operation is not allowed.",

            StatusCodes.Status404NotFound =>
                "The requested resource was not found.",

            StatusCodes.Status409Conflict =>
                "The request conflicts with the current state.",

            StatusCodes.Status500InternalServerError =>
                "An unexpected error occurred.",

            _ =>
                "The request could not be completed."
        };
    }
}