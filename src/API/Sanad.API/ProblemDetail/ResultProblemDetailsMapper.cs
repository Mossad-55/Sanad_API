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

            ["Identity.Elderly.PhoneAlreadyInUse"] = 409,
            ["Identity.Elderly.NotFound"] = 404,
            ["Identity.Elderly.InvalidProfile"] = 400,

            ["Identity.User.EmailNotFound"] = 404,


            ["Cms.Splash.InternalNameAlreadyInUse"] = 409,
            ["Cms.Splash.NotFound"] = 404,

            ["Caregivers.Lookups.NameAlreadyInUse"] = 409,
            ["Caregivers.Lookups.LanguageCodeInUse"] = 409,
            ["Caregivers.Lookups.NotFound"] = 404,
            ["Caregivers.Lookups.ParentNotFound"] = 404,
            ["Caregivers.Lookups.ParentNotActive"] = 409,

            ["Caregivers.Onboarding.AlreadyExists"] = 409,
            ["Caregivers.Onboarding.NotFound"] = 404,
            ["Caregivers.Onboarding.WrongCaregiverType"] = 409,
            ["Caregivers.Onboarding.InactiveLookup"] = 409,
            ["Caregivers.Onboarding.InvalidSchedule"] = 409,
            ["Caregivers.Onboarding.NotActive"] = 409,
            ["Caregivers.Onboarding.CertificateNotFound"] = 404,
            ["Caregivers.Onboarding.InvalidCertificateOperation"] = 409,
            ["Caregivers.Onboarding.InvalidState"] = 409,
            ["Caregivers.Onboarding.CaregiverNotFound"] = 404,

            ["Families.Family.AlreadyExists"] = 409,
            ["Families.Family.NotFound"] = 404,
            ["Families.Family.InvalidName"] = 400,
            ["Families.Family.NotOwner"] = 403,
            ["Families.Family.AccessDenied"] = 403,

            ["Families.Elderly.FamilyNotFound"] = 404,
            ["Families.Elderly.NotFound"] = 404,
            ["Families.Elderly.PhoneLinkedToAnotherFamily"] = 409,
            ["Families.Elderly.PhoneBelongsToNonElderly"] = 409,
            ["Families.Elderly.IdentityCreationFailed"] = 409,
            ["Families.Elderly.InvalidProfile"] = 400,
            ["Families.Elderly.AccessDenied"] = 403,

            ["Families.Invitation.FamilyNotFound"] = 404,
            ["Families.Invitation.AccessDenied"] = 403,
            ["Families.Invitation.RecipientNotRegistered"] = 409,
            ["Families.Invitation.RecipientMissingFamilyAccount"] = 409,
            ["Families.Invitation.CannotInviteYourself"] = 409,
            ["Families.Invitation.AlreadyMember"] = 409,
            ["Families.Invitation.PendingInvitationExists"] = 409,
            ["Families.Invitation.InvalidRole"] = 400,
            ["Families.Invitation.InvalidToken"] = 400,
            ["Families.Invitation.NotFound"] = 404,
            ["Families.Invitation.NotPending"] = 409,
            ["Families.Invitation.NotInvitee"] = 403,
            ["Families.Invitation.Expired"] = 409,

            ["Families.Assessment.QuestionNotFound"] = 404,
            ["Families.Assessment.TierNotFound"] = 404,
            ["Families.Assessment.NotFound"] = 404,
            ["Families.Assessment.InvalidQuestion"] = 409,
            ["Families.Assessment.InvalidTier"] = 409,
            ["Families.Assessment.InvalidSubmission"] = 409,

            ["Bookings.FamilyNotFound"] = 404,
            ["Bookings.UnauthorizedRole"] = 403,
            ["Bookings.ElderlyNotFound"] = 404,
            ["Bookings.ScheduleConflict"] = 409,
            ["Bookings.NotFound"] = 404,
            ["Bookings.BookingNotInFamily"] = 404,
            ["Bookings.Domain.InvalidOperation"] = 409,
            ["Bookings.PriceUnavailable"] = 409,

            ["Caregivers.Discovery.CaregiverNotFound"] = 404,
            ["Caregivers.Discovery.QuoteNotAvailable"] = 409,

            ["Storage.File.Empty"] = 400,
            ["Storage.File.TooLarge"] = 400,
            ["Storage.File.UnsupportedType"] = 400,
            ["Storage.File.NotFound"] = 404
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