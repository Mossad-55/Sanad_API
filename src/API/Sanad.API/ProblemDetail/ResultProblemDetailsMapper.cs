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

            // F7 caregiver-rating moderation: an unknown rating is a 404, a bad
            // reason or filter a 400, and an illegal visibility transition a 409.
            ["Caregivers.Rating.NotFound"] = 404,
            ["Caregivers.Rating.InvalidReason"] = 400,
            ["Caregivers.Rating.InvalidQuery"] = 400,
            ["Caregivers.Rating.AlreadyHidden"] = 409,
            ["Caregivers.Rating.AlreadyVisible"] = 409,

            ["Caregivers.Payouts.NotFound"] = 404,
            ["Caregivers.Payouts.Ineligible"] = 409,
            ["Caregivers.Payouts.Conflict"] = 409,
            ["Caregivers.Payouts.InvalidState"] = 409,
            ["Caregivers.Payouts.AccountNotVerified"] = 409,

            ["CareHomes.Rating.BookingNotEligible"] = 404,
            ["CareHomes.Rating.Conflict"] = 409,

            // F7 Care Home rating moderation + owner flag: an unknown rating (or
            // a rating that is not about the caller's facility) is a 404, a bad
            // reason/filter a 400, and a refusal to hide/keep/restore a 409.
            ["CareHomes.Rating.NotFound"] = 404,
            ["CareHomes.Rating.InvalidReason"] = 400,
            ["CareHomes.Rating.InvalidQuery"] = 400,
            ["CareHomes.Rating.AlreadyHidden"] = 409,
            ["CareHomes.Rating.AlreadyVisible"] = 409,
            ["CareHomes.Rating.NoOpenFlag"] = 409,

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
            ["CareHomes.Inventory.MediaInvalid"] = 400,
            ["CareHomes.Inventory.MediaNotFound"] = 404,
            ["CareHomes.Inventory.RoomMediaRequired"] = 400,
            ["CareHomes.Discovery.NotFound"] = 404,
            ["CareHomes.Discovery.InvalidQuery"] = 400,
            ["CareHomes.Bookings.NotFound"] = 404,
            ["CareHomes.Bookings.InvalidState"] = 409,
            ["CareHomes.Bookings.CapacityConflict"] = 409,
            ["CareHomes.Bookings.PaymentConflict"] = 409,
            ["CareHomes.Bookings.ChargesNotConfigured"] = 503,
            ["CareHomes.Bookings.RefundNotRetryable"] = 409,
            ["CareHomes.Bookings.InvalidNote"] = 400,
            ["CareHomes.Revenue.InvalidRange"] = 400,
            ["CareHomes.Bookings.InvalidAssignment"] = 400,
            ["CareHomes.Bookings.AssignmentConflict"] = 409,
            ["CareHomes.Bookings.InvalidTransferDate"] = 400,
            ["CareHomes.Bookings.TransferConflict"] = 409,
            ["CareHomes.Bookings.IdempotencyKeyInvalid"] = 400,
            ["CareHomes.Bookings.IdempotencyConflict"] = 409,

            // S5 care-home refund policy: unreadable persisted policy state fails closed as a
            // conflict, while reconciliation errors distinguish malformed requests from attempts
            // that cannot be proven dead.
            ["CareHomes.Bookings.UnreadableSnapshot"] = 409,
            ["CareHomes.Refund.OutcomeAmbiguous"] = 409,
            ["CareHomes.RefundReconciliation.EvidenceRequired"] = 400,
            ["CareHomes.RefundReconciliation.InvalidAttemptClaim"] = 400,
            ["CareHomes.RefundReconciliation.EvidenceTooLong"] = 400,
            ["CareHomes.RefundReconciliation.AttemptMismatch"] = 409,
            ["CareHomes.RefundReconciliation.AttemptInconclusive"] = 409,
            ["CareHomes.RefundReconciliation.ConcurrencyConflict"] = 409,
            ["CareHomes.RefundReconciliation.NoWindowToReconcile"] = 409,
            ["CareHomes.Visits.NotFound"] = 404,
            ["CareHomes.Visits.InvalidRequest"] = 400,
            ["CareHomes.Visits.InvalidState"] = 409,
            ["CareHomes.Visits.Conflict"] = 409,
            ["CareHomes.Visits.CapacityConflict"] = 409,

            // P2 visit-create idempotency: a reused key with different create
            // content conflicts, like the partner-contact surface.
            ["CareHomes.Visits.IdempotencyConflict"] = 409,
            ["CareHomes.CheckInDispute.NotFound"] = 404,
            ["CareHomes.CheckInDispute.InvalidState"] = 409,
            ["CareHomes.CheckInDispute.InvalidReason"] = 400,
            ["CareHomes.CheckInDispute.OpenConflict"] = 409,
            ["CareHomes.CheckInDispute.InvalidQuery"] = 400,

            ["Identity.Verification.ResendRequestNotFound"] = 404,
            ["Identity.Verification.ResendRequestNotPending"] = 400,
            ["Identity.Verification.RequestSuperseded"] = 409,
            ["Identity.Verification.ResendCooldownActive"] = 409,
            ["Identity.Verification.ProviderDeliveryFailed"] = 502,
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

            // F5 support inbox: an unknown ticket id is a real 404 (the owner's
            // own-ticket read deliberately reports another user's ticket the
            // same way), and a refused status transition or a reply to a closed
            // ticket is a 409.
            ["Identity.Support.NotFound"] = 404,
            ["Identity.Support.Conflict"] = 409,

            // Anonymous partner contact: the caller's own bad request is a
            // 400, a reused idempotency key with different content is a 409, an
            // unknown contact id in the admin inbox is a 404, and the
            // anti-spam window is a 429.
            ["Identity.PartnerContact.Invalid"] = 400,
            ["Identity.PartnerContact.NotFound"] = 404,
            ["Identity.PartnerContact.IdempotencyConflict"] = 409,
            ["Identity.PartnerContact.RateLimited"] = 429,

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

            ["Caregivers.Lookups.NameAlreadyInUse"] = 409,
            ["Caregivers.Lookups.LanguageCodeInUse"] = 409,
            ["Caregivers.Lookups.BankCodeInUse"] = 409,
            ["Caregivers.Lookups.NotFound"] = 404,
            ["Caregivers.Lookups.ParentNotFound"] = 404,
            ["Caregivers.Lookups.ParentNotActive"] = 409,

            ["Caregivers.PayoutAccount.NotFound"] = 404,
            ["Caregivers.PayoutAccount.InvalidIban"] = 400,
            ["Caregivers.PayoutAccount.UnknownBank"] = 400,
            ["Caregivers.PayoutAccount.InactiveBank"] = 409,
            ["Caregivers.PayoutAccount.InvalidState"] = 409,
            ["Caregivers.PayoutAccount.RevisionConflict"] = 409,
            ["Caregivers.PayoutAccount.ProtectionUnavailable"] = 503,

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

            ["Families.CareRecords.AccessDenied"] = 403,
            ["Families.CareRecords.DependentNotFound"] = 404,
            ["Families.CareRecords.NotFound"] = 404,
            ["Families.CareRecords.Invalid"] = 400,
            ["Families.CareRecords.InvalidQuery"] = 400,
            ["Families.CareRecords.ConcurrencyConflict"] = 409,

            // M4 retry-safe creates: a malformed Idempotency-Key is a request
            // error that writes nothing, and reusing a key with a different
            // request is a conflict.
            ["Families.CareRecords.IdempotencyKeyInvalid"] = 400,
            ["Families.CareRecords.IdempotencyConflict"] = 409,

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
            ["Bookings.AvailabilityConflict"] = 409,
            ["Bookings.IdempotencyKeyInvalid"] = 400,
            ["Bookings.IdempotencyConflict"] = 409,
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

            // S4 versioned care-booking refunds: an award above the money still
            // refundable is a conflict with the booking's refund ledger, while a
            // provider movement that did not complete is retryable by ops (503).
            ["Bookings.RefundExceedsCeiling"] = 409,
            ["Bookings.RefundSettlementIncomplete"] = 503,
            ["Bookings.RefundOutcomeAmbiguous"] = 409,
            ["Bookings.Outcome.InvalidReason"] = 400,
            ["Bookings.Outcome.ReasonNotAllowed"] = 400,
            ["Bookings.RefundReconciliation.EvidenceRequired"] = 400,
            ["Bookings.RefundReconciliation.EvidenceTooLong"] = 400,
            ["Bookings.RefundReconciliation.ProviderReferenceRequired"] = 400,
            ["Bookings.RefundReconciliation.NoAmbiguousAttempt"] = 409,
            ["Bookings.RefundReconciliation.AwardUnknown"] = 409,
            ["Bookings.RefundReconciliation.AttemptMismatch"] = 409,
            ["Bookings.RefundReconciliation.ClaimInFlight"] = 409,
            ["Bookings.RefundReconciliation.ConcurrencyConflict"] = 409,

            ["Caregivers.Discovery.CaregiverNotFound"] = 404,
            ["Caregivers.Discovery.QuoteNotAvailable"] = 409,
            ["Caregivers.Photo.NotFound"] = 404,
            ["Caregivers.Availability.InvalidQuery"] = 400,

            ["Paymob.NotConfigured"] = 503,
            ["Paymob.MethodNotAvailable"] = 409,
            ["Paymob.GatewayError"] = 502,

            // Portal second pass B1: the two gateway refund answers the shared
            // mapper previously fell through to 400. A definitive provider
            // refusal is a gateway-level failure (502) that proved no money
            // moved, and an unconfirmable outcome is an operator-reconcilable
            // state (409). The stable codes stay on the wire, status alone
            // grants no retry: the booking's ledger/state fences (claim,
            // ambiguity marker, refundState) remain authoritative.
            ["Paymob.RefundRejected"] = 502,
            ["Paymob.RefundOutcomeUnknown"] = 409,

            // E04 platform controls: maintenance blocks new checkout/payment
            // starts (503), a disabled/unconfigured method is a client error
            // (400), a stale control update conflicts (409), and an unreadable
            // control record fails closed (503).
            ["Platform.Maintenance"] = 503,
            ["Platform.PaymentMethodUnavailable"] = 400,
            ["Platform.ControlVersionConflict"] = 409,
            ["Platform.ControlStateUnavailable"] = 503,
            ["Platform.Control.Invalid"] = 400,

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
            , ["MedicalAccess.NotFound"] = 404
            , ["MedicalAccess.RecordAccessDenied"] = 403
            , ["Bookings.EmergencyContactNotAllowed"] = 403
            , ["Families.AccessDenied"] = 403
            , ["Families.AdminMedication.NotFound"] = 404
            , ["Families.Sos.NotFound"] = 404
            , ["Families.Sos.IdempotencyConflict"] = 409
            , ["Families.Sos.InvalidOperation"] = 409
            , ["Families.Sos.InvalidLocation"] = 400
            , ["Families.AuditWriteFailed"] = 503

            , ["Cms.ElderlyWelcome.NotFound"] = 404
            , ["Cms.ElderlyWelcome.NotPublished"] = 404
            , ["Cms.ElderlyWelcome.AlreadyExists"] = 409
            , ["Cms.ElderlyWelcome.InvalidState"] = 409
            , ["Cms.ElderlyWelcome.Invalid"] = 400
            , ["Cms.LibraryCategory.NotFound"] = 404
            , ["Cms.LibraryCategory.Invalid"] = 400
            , ["Cms.LibraryCategory.InUse"] = 409
            , ["Cms.LibraryContent.NotFound"] = 404
            , ["Cms.LibraryContent.NotPublished"] = 404
            , ["Cms.LibraryContent.Invalid"] = 400
            , ["Cms.LibraryContent.InvalidOperation"] = 409
            , ["Cms.Library.InvalidCategory"] = 400
            , ["Cms.Library.CategoryInactive"] = 409
            , ["Cms.LandingSection.NotFound"] = 404
            , ["Cms.LandingSection.Invalid"] = 400
            , ["Cms.Landing.InvalidType"] = 400
            , ["Cms.LandingSection.Incomplete"] = 400
            , ["Cms.LandingSection.PublishedHistory"] = 409
            , ["Cms.Landing.ReorderInvalid"] = 400
            , ["Feedback.InvalidRating"] = 400
            , ["Community.PostNotFound"] = 404
            , ["Community.InvalidModerationOperation"] = 409
            , ["Community.CheckIn.Invalid"] = 400
            , ["Community.Image.Invalid"] = 400
            , ["Community.Image.NotFound"] = 404
            , ["Community.Author.Photo.NotFound"] = 404
            , ["Community.CategoryNotFound"] = 404
            , ["Community.CategoryInactive"] = 409
            , ["Community.CategoryInUse"] = 409
            , ["Community.InvalidCategory"] = 400
            , ["Community.Category.Invalid"] = 400

            // W3-F3 subscription refunds: a missing case/family/subscription or
            // capture is a malformed request, an award above that capture's
            // remaining funds is a ledger conflict, and a reused idempotency
            // key with different content is a conflict.
            , ["Finance.SubscriptionRefund.CaseRequired"] = 400
            , ["Finance.SubscriptionRefund.FamilyRequired"] = 400
            , ["Finance.SubscriptionRefund.SubscriptionRequired"] = 400
            , ["Finance.SubscriptionRefund.CaptureRequired"] = 400
            , ["Finance.SubscriptionRefund.AmountInvalid"] = 400
            , ["Finance.SubscriptionRefund.Invalid"] = 400
            , ["Finance.SubscriptionRefund.IdempotencyConflict"] = 409
            , ["Finance.SubscriptionRefund.ExceedsCaptured"] = 409
            , ["Finance.SubscriptionRefund.Conflict"] = 409
            , ["Finance.SubscriptionRefund.Unknown"] = 500

            // W3-F3 complaint review: approval is a SuperAdmin act, an invalid
            // transition is a conflict, and a missing case is a 404.
            , ["Finance.Complaint.FamilyRequired"] = 400
            , ["Finance.Complaint.CaseRequired"] = 400
            , ["Finance.Complaint.AmountInvalid"] = 400
            , ["Finance.Complaint.AwardExceedsCap"] = 409
            , ["Finance.Complaint.NotFound"] = 404
            , ["Finance.Complaint.Invalid"] = 409
            , ["Finance.Complaint.SuperAdminRequired"] = 403
            , ["Finance.Complaint.Conflict"] = 409
            , ["Finance.Complaint.Unknown"] = 500
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

        if (error.Code == "Identity.Verification.ProviderDeliveryFailed")
        {
            problemDetails.Detail =
                "Verification code delivery is temporarily unavailable. " +
                "Please try again.";
        }

        problemDetails.Extensions["code"] =
            error.Code;

        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        // Field-level validation detail is attached only when the error
        // actually carries it — an empty map must never surface as an empty
        // "errors" object. The dictionary shape matches the one
        // AdminLibraryController already emits, and every error without field
        // detail produces an unchanged problem-details body.
        if (error.Errors is { Count: > 0 })
        {
            problemDetails.Extensions["errors"] =
                error.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value);
        }

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

            StatusCodes.Status502BadGateway =>
                "Bad Gateway",

            StatusCodes.Status503ServiceUnavailable =>
                "Service Unavailable",

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

            StatusCodes.Status503ServiceUnavailable =>
                "The service is temporarily unavailable.",

            StatusCodes.Status500InternalServerError =>
                "An unexpected error occurred.",

            _ =>
                "The request could not be completed."
        };
    }
}
