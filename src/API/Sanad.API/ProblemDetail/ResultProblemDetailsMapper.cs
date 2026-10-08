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
            ["Identity.ElderlyLogin.AccountNotRegistered"] = 404,
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

            ["Caregivers.Rating.BookingNotEligible"] = 404,
            ["Caregivers.Rating.CaregiverNotFound"] = 404,
            ["Caregivers.Rating.Conflict"] = 409,

            ["CareHomes.Rating.BookingNotEligible"] = 404,
            ["CareHomes.Rating.Conflict"] = 409,

            ["CareHomes.Facility.AlreadyExists"] = 409,
            ["CareHomes.Facility.NotFound"] = 404,
            ["CareHomes.Facility.AccessDenied"] = 403,
            ["CareHomes.Facility.Conflict"] = 409,
            ["CareHomes.Facility.InvalidOwner"] = 400,
            ["CareHomes.Facility.InvalidProfile"] = 400,
            ["CareHomes.Facility.InvalidSubmission"] = 400,
            ["CareHomes.Document.FacilityNotFound"] = 404,
            ["CareHomes.Document.Conflict"] = 409,
            ["CareHomes.Document.InvalidContent"] = 400,
            ["CareHomes.Admin.InvalidQuery"] = 400,
            ["CareHomes.Admin.ApplicationNotFound"] = 404,
            ["CareHomes.Admin.DocumentNotFound"] = 404,
            ["CareHomes.Admin.PrivateDocumentUnavailable"] = 409,
            ["CareHomes.Admin.Conflict"] = 409,
            ["CareHomes.Admin.InvalidOperation"] = 409,

            ["CareHomes.Inventory.NotFound"] = 404,
            ["CareHomes.Inventory.Conflict"] = 409,
            ["CareHomes.Inventory.RoomNumberTaken"] = 409,
            ["CareHomes.Inventory.LabelTaken"] = 409,
            ["CareHomes.Inventory.ActiveOccupancy"] = 409,
            ["CareHomes.Inventory.Overlap"] = 409,
            ["CareHomes.Inventory.Invalid"] = 400,
            ["CareHomes.Inventory.InvalidTarget"] = 400,
            ["CareHomes.Inventory.InvalidRange"] = 400,
            ["CareHomes.Inventory.InvalidState"] = 409,
            ["CareHomes.Discovery.NotFound"] = 404,
            ["CareHomes.Discovery.InvalidQuery"] = 400,
            ["CareHomes.Bookings.NotFound"] = 404,
            ["CareHomes.Bookings.InvalidState"] = 409,
            ["CareHomes.Bookings.CapacityConflict"] = 409,
            ["CareHomes.Bookings.PaymentConflict"] = 409,
            ["CareHomes.Bookings.ChargesNotConfigured"] = 503,
            ["CareHomes.Bookings.RefundNotRetryable"] = 409,
            ["CareHomes.Bookings.InvalidAssignment"] = 400,
            ["CareHomes.Bookings.AssignmentConflict"] = 409,
            ["CareHomes.Bookings.InvalidTransferDate"] = 400,
            ["CareHomes.Bookings.TransferConflict"] = 409,
            ["CareHomes.CheckInDispute.NotFound"] = 404,
            ["CareHomes.CheckInDispute.InvalidState"] = 409,
            ["CareHomes.CheckInDispute.InvalidReason"] = 400,
            ["CareHomes.CheckInDispute.OpenConflict"] = 409,

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

            ["Identity.IdentityDocument.UserNotFound"] = 404,
            ["Identity.IdentityDocument.NotFound"] = 404,
            ["Identity.IdentityDocument.UnsupportedAccountType"] = 409,
            ["Identity.IdentityDocument.InvalidOperation"] = 409,

            ["Identity.Avatar.UserNotFound"] = 404,
            ["Identity.Avatar.NotFound"] = 404,
            ["Identity.Avatar.UnsupportedAccountType"] = 409,
            ["Identity.Avatar.InvalidOperation"] = 409,

            ["Identity.Account.UnsupportedAccountType"] = 403,
            ["Identity.Account.AccountAlreadyExists"] = 409,
            ["Identity.Account.CaregiverTypesExclusive"] = 409,
            ["Identity.Account.AccountNotOwned"] = 403,
            ["Identity.Account.RetainedCaregiverProfile"] = 409,
            ["Identity.Account.UserNotFound"] = 404,
            ["Identity.Account.InvalidOperation"] = 409,
            ["Identity.Account.CaregiverOnly"] = 403,
            ["Identity.Account.CaregiverProfileNotFound"] = 404,
            ["Identity.Account.ActiveBookingExists"] = 409,
            ["Identity.Account.ElderlyManagedByFamily"] = 409,
            ["Identity.Account.OwnershipTransferRequired"] = 409,

            ["Cms.Splash.InternalNameAlreadyInUse"] = 409,
            ["Cms.Splash.NotFound"] = 404,

            ["Cms.Content.UnsupportedAudience"] = 403,

            ["Cms.Legal.NotFound"] = 404,
            ["Cms.Legal.NotPublished"] = 404,
            ["Cms.Legal.DraftAlreadyExists"] = 409,
            ["Cms.Legal.PublishedDocumentImmutable"] = 409,
            ["Cms.Legal.InvalidOperation"] = 409,
            ["Cms.WellnessTip.NotFound"] = 404,
            ["Cms.WellnessTip.NotPublished"] = 404,
            ["Cms.WellnessTip.InvalidOperation"] = 409,
            ["Cms.MedicationLateness.NotFound"] = 404,

            ["Cms.SentenceBuilder.NotFound"] = 404,
            ["Cms.SentenceBuilder.InvalidOperation"] = 409,
            ["Cms.SentenceBuilder.Conflict"] = 409,

            ["Cms.Help.FaqNotFound"] = 404,
            ["Cms.Help.SupportContactNotFound"] = 404,

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

            ["Caregivers.Deactivation.CaregiverNotFound"] = 404,

            ["Families.Family.AlreadyExists"] = 409,
            ["Families.Family.NotFound"] = 404,
            ["Families.Family.InvalidName"] = 400,
            ["Families.Family.NotOwner"] = 403,
            ["Families.Family.AccessDenied"] = 403,
            ["Families.Family.MemberNotFound"] = 404,
            ["Families.Family.OwnerProtected"] = 409,

            ["Families.Notes.DependentNotFound"] = 404,
            ["Families.Notes.AccessDenied"] = 403,
            ["Families.Notes.NotFound"] = 404,
            ["Families.Notes.InvalidNote"] = 400,

            ["Families.Medication.DependentNotFound"] = 404,
            ["Families.Medication.AccessDenied"] = 403,
            ["Families.Medication.NotFound"] = 404,
            ["Families.Medication.InvalidDateRange"] = 400,

            ["Families.Family.AcknowledgementRequired"] = 400,
            ["Families.Family.ActiveBookingExists"] = 409,
            ["Families.Family.UnsettledPaymentExists"] = 409,

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
            ["Bookings.AllowanceExceeded"] = 409,
            ["Bookings.AllowanceConcurrency"] = 409,
            ["Bookings.PriceUnavailable"] = 409,
            ["Bookings.AlreadyRefunded"] = 409,
            ["Bookings.RefundNotEligible"] = 409,
            ["Bookings.NoRefundDue"] = 409,
            ["Bookings.Cancel.AlreadyProcessed"] = 409,

            ["Caregivers.Discovery.CaregiverNotFound"] = 404,
            ["Caregivers.Discovery.QuoteNotAvailable"] = 409,

            ["Paymob.NotConfigured"] = 503,
            ["Paymob.MethodNotAvailable"] = 409,
            ["Paymob.GatewayError"] = 502,

            ["Storage.File.Empty"] = 400,
            ["Storage.File.TooLarge"] = 400,
            ["Storage.File.UnsupportedType"] = 400,
            ["Storage.File.NotFound"] = 404,

            ["Notifications.NotFound"] = 404,
            ["Notifications.AdminNotification.NotFound"] = 404,
            ["Families.AdminCheckIn.NotFound"] = 404,
            ["Families.AdminCheckIn.InvalidDateRange"] = 400,
            ["Families.ElderlyCheckIn.NotFound"] = 404,
            ["Families.ElderlyCheckIn.InvalidTimeZone"] = 409,
            ["Families.ElderlyCheckIn.AlreadyAnswered"] = 409,
            ["Families.HelpRequest.NotFound"] = 404,
            ["Families.HelpRequest.IdempotencyConflict"] = 409,
            ["Families.HelpRequest.InvalidOperation"] = 409
            , ["Bookings.NotFound"] = 404
            , ["Bookings.AccessDenied"] = 403
            , ["Bookings.ReviewNotAllowed"] = 409
            , ["Bookings.ReviewExists"] = 409
            , ["Bookings.InvalidReview"] = 400
            , ["Caregivers.NotFound"] = 404
            , ["Caregivers.AccessDenied"] = 403
            , ["Caregivers.HelpRequest.NotFound"] = 404
            , ["Caregivers.HelpRequest.InvalidOperation"] = 409
            , ["Caregivers.HelpRequest.InvalidReason"] = 400
            , ["Caregivers.HelpRequest.InvalidDateRange"] = 400
            , ["Caregivers.MedicationTask.NotFound"] = 404
            , ["Caregivers.MedicationTask.InvalidOperation"] = 409
            , ["Caregivers.MedicationTask.InvalidReason"] = 400
            , ["MedicalAccess.GrantNotFound"] = 404
            , ["MedicalAccess.GrantExists"] = 409
            , ["MedicalAccess.InvalidGrant"] = 400
            , ["Families.AccessDenied"] = 403
            , ["Families.AdminMedication.NotFound"] = 404
            , ["Families.Sos.NotFound"] = 404
            , ["Families.Sos.IdempotencyConflict"] = 409
            , ["Families.Sos.InvalidOperation"] = 409
            , ["Families.Sos.InvalidLocation"] = 400

            , ["Cms.ElderlyWelcome.NotFound"] = 404
            , ["Cms.ElderlyWelcome.NotPublished"] = 404
            , ["Cms.ElderlyWelcome.AlreadyExists"] = 409
            , ["Cms.ElderlyWelcome.InvalidState"] = 409
            , ["Cms.ElderlyWelcome.Invalid"] = 400
            , ["Feedback.InvalidRating"] = 400
            , ["Community.PostNotFound"] = 404
            , ["Community.InvalidModerationOperation"] = 409
            , ["Community.CheckIn.Invalid"] = 400
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

        if (error.Code == "Identity.ElderlyLogin.AccountNotRegistered")
        {
            problemDetails.Detail = "Elderly account not registered.";
        }

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
