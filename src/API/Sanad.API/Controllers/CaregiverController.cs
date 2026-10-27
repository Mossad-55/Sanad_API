using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Application.Privacy;
using Sanad.Modules.Caregivers.Application.HelpRequests;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Domain.HelpRequests;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver")]
public sealed class CaregiverController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;
    private readonly IDateTimeProvider _clock;

    public CaregiverController(
        ISender sender,
        IFileStorage fileStorage,
        IDateTimeProvider clock)
    {
        _sender = sender;
        _fileStorage = fileStorage;
        _clock = clock;
    }

    [HttpPost("profile")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> BootstrapProfile(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId) ||
            !TryGetCaregiverTypeFromClaims(out CaregiverType type))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BootstrapCaregiverCommand(
                    userId,
                    type),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet("profile")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetCaregiverProfileQuery(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalProfile(
        [FromBody] UpdateMedicalProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalProfileCommand(
                    userId,
                    new ProfessionalTitleId(
                        request.ProfessionalTitleId),
                    request.YearsOfExperience,
                    new SpecializationId(
                        request.SpecializationId),
                    new AcademicDegreeId(
                        request.AcademicDegreeId),
                    request.CurrentWorkplace,
                    request.Biography,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionProfile(
        [FromBody] UpdateCompanionProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionProfileCommand(
                    userId,
                    request.YearsOfExperience,
                    new SpecializationId(
                        request.SpecializationId),
                    request.Biography,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/address")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAddress(
        [FromBody] UpdateCaregiverAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCaregiverAddressCommand(
                    userId,
                    request.DetailedAddress),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("selections")]
    [ProducesResponseType(
    typeof(CaregiverProfileResponse),
    StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSelections(
    [FromBody] UpdateCaregiverSelectionsRequest request,
    CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    request.ServiceIds,
                    request.LanguageIds,
                    request.AreaIds),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("pricing/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalPricing(
        [FromBody] UpdateMedicalPricingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalPricingCommand(
                    userId,
                    request.HomeVisitPrice,
                    request.EightHourShiftPrice,
                    request.TwelveHourShiftPrice,
                    request.TwentyFourHourShiftPrice),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("pricing/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionPricing(
        [FromBody] UpdateCompanionPricingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionPricingCommand(
                    userId,
                    request.HourlyPrice,
                    request.EightHourDayPrice,
                    request.OvernightPrice),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("schedule/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalShedule(
        [FromBody] UpdateMedicalScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalScheduleCommand(
                    userId,
                    (request.Shifts ?? [])
                        .Select(shift =>
                            new MedicalShiftItem(
                                (DayOfWeek)shift.DayOfWeek,
                                (MedicalShiftType)shift.ShiftType))
                        .ToList(),
                    (request.HomeVisitWindows ?? [])
                        .Select(window =>
                            new MedicalHomeVisitWindowItem(
                                (DayOfWeek)window.DayOfWeek,
                                window.StartTime,
                                window.EndTime))
                        .ToList()),
                    cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("schedule/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionSchedule(
        [FromBody] UpdateCompanionScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionScheduleCommand(
                    userId,
                    (request.Windows ?? [])
                        .Select(window =>
                            new CompanionAvailabilityWindowItem(
                                (CompanionBookingType)window.BookingType,
                                (DayOfWeek)window.DayOfWeek,
                                window.StartTime,
                                window.EndTime))
                        .ToList()),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("availability/available")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> BecomeAvailable(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BecomeAvailableCommand(
                    userId,
                    CaregiverCertificateClock.CairoDate(_clock)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("availability/unavailable")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> BecomeUnavailable(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BecomeUnavailableCommand(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("certificates")]
    [RequestSizeLimit(5_242_880)] // 5 MB, matches private storage limit
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
    typeof(CaregiverProfileResponse),
    StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCertificate(
    [FromForm] AddCertificateRequest request,
    IFormFile? file,
    CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        Result<StoredFile> upload =
            await SavePrivateCertificateAsync(
                file,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string fileKey = upload.Value.Key;

        var result =
            await _sender.Send(
                new AddCertificateCommand(
                    userId,
                    request.Type,
                    fileKey,
                    request.ExpiryDate,
                    CaregiverCertificateClock.CairoDate(_clock),
                    _clock.UtcNow),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                fileKey,
                cancellationToken);

            return ToActionResult(result);
        }

        return ToActionResult(result);
    }

    [HttpPut("certificates/{certificateId:guid}/file")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplaceCertificateFile(
        Guid certificateId,
        [FromForm] ReplaceCertificateFileRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        Result<StoredFile> upload =
            await SavePrivateCertificateAsync(
                file,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string fileKey = upload.Value.Key;

        var result =
            await _sender.Send(
                new ReplaceCertificateFileCommand(
                    userId,
                    new CaregiverCertificateId(certificateId),
                    fileKey,
                    request.ExpiryDate,
                    CaregiverCertificateClock.CairoDate(_clock),
                    _clock.UtcNow),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                fileKey,
                cancellationToken);

            return ToActionResult(result);
        }

        return ToActionResult(result);
    }

    [HttpDelete("certificates/{certificateId:guid}")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveCertificate(
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RemoveCertificateCommand(
                    userId,
                    new CaregiverCertificateId(certificateId)),
                cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Logically removes one superseded history version of the caregiver's own
    /// qualification. The version's row and stored file are never physically
    /// deleted; they are hidden from the normal read surfaces and can never be
    /// promoted, verified or reanimated. Idempotent (204) when the version is
    /// already removed by this caregiver. 409 when the version is the current
    /// effective version (even expired), the live pending candidate or the
    /// version still backing the effective snapshot; 404 when the version is
    /// unknown or belongs to another caregiver.
    /// </summary>
    [HttpDelete(
        "certificates/{certificateId:guid}/versions/{versionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveCertificateVersion(
        Guid certificateId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RemoveOwnCertificateVersionCommand(
                    userId,
                    new CaregiverCertificateId(certificateId),
                    new CaregiverCertificateVersionId(versionId),
                    _clock.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Streams one stored certificate scan of the caregiver's own
    /// qualification: the named version when supplied, otherwise the effective
    /// snapshot version (approved current, else live pending, else the newest
    /// retained candidate). A removed version is answered with the generic 404
    /// (its bytes stay retained for audit but are no longer downloadable). The
    /// route carries no storage key and is scoped to the authenticated owner.
    /// </summary>
    [HttpGet("certificates/{certificateId:guid}/versions/{versionId:guid}/file")]
    public async Task<IActionResult> DownloadCertificateVersionFile(
        Guid certificateId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetCertificateFileQuery(
                    CaregiverId.Empty,
                    new CaregiverCertificateId(certificateId),
                    new CaregiverCertificateVersionId(versionId),
                    userId),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        // FileStreamResult disposes the stream after sending.
        return File(
            result.Value.Content,
            result.Value.ContentType,
            result.Value.FileName);
    }

    [HttpGet("certificates/{certificateId:guid}/file")]
    public async Task<IActionResult> DownloadCertificateFile(
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetCertificateFileQuery(
                    CaregiverId.Empty,
                    new CaregiverCertificateId(certificateId),
                    VersionId: null,
                    OwnerUserId: userId),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        // FileStreamResult disposes the stream after sending.
        return File(
            result.Value.Content,
            result.Value.ContentType,
            result.Value.FileName);
    }

    [HttpPost("submit")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
            StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitForReview(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new SubmitCaregiverCommand(
                    userId,
                    CaregiverCertificateClock.CairoDate(_clock),
                    _clock.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("privacy")]
    [ProducesResponseType(
        typeof(CaregiverPrivacyResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrivacy(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetCaregiverPrivacyQuery(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("privacy")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePrivacy(
        [FromBody] UpdateCaregiverPrivacyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCaregiverPrivacyCommand(
                    userId,
                    request.ShowProfile!.Value,
                    request.ShowRating!.Value,
                    request.ShowPhone!.Value,
                    request.ShareLocation!.Value),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return NoContent();
    }

    // -------------------------- Help Requests --------------------------

    [HttpGet("help-requests")]
    [ProducesResponseType(typeof(PagedCaregiverHelpRequests), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHelpRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ElderlyHelpRequestStatus? status = null,
        [FromQuery] Guid? elderlyId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new GetCaregiverHelpRequestsQuery(
                userId,
                page,
                pageSize,
                status,
                elderlyId),
            cancellationToken));
    }

    [HttpGet("help-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(CaregiverHelpRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHelpRequest(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new GetCaregiverHelpRequestQuery(
                userId,
                requestId),
            cancellationToken));
    }

    [HttpPost("help-requests/{requestId:guid}/accept")]
    [ProducesResponseType(typeof(CaregiverHelpRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptHelpRequest(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new AcceptCaregiverHelpRequestCommand(
                userId,
                requestId),
            cancellationToken));
    }

    [HttpPost("help-requests/{requestId:guid}/decline")]
    [ProducesResponseType(typeof(CaregiverHelpRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineHelpRequest(
        Guid requestId,
        [FromBody] CaregiverHelpRequestReasonRequest? body,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new DeclineCaregiverHelpRequestCommand(
                userId,
                requestId,
                body?.Reason ?? string.Empty),
            cancellationToken));
    }

    [HttpPost("help-requests/{requestId:guid}/start")]
    [ProducesResponseType(typeof(CaregiverHelpRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartHelpRequest(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new StartCaregiverHelpRequestCommand(
                userId,
                requestId),
            cancellationToken));
    }

    [HttpPost("help-requests/{requestId:guid}/resolve")]
    [ProducesResponseType(typeof(CaregiverHelpRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveHelpRequest(
        Guid requestId,
        [FromBody] CaregiverHelpRequestReasonRequest? body,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new ResolveCaregiverHelpRequestCommand(
                userId,
                requestId,
                body?.Reason ?? string.Empty),
            cancellationToken));
    }

    // 5 MB, the documented certificate limit and the private-storage ceiling.
    private const long MaximumCertificateBytes =
        5_242_880;

    /// <summary>
    /// Stores a certificate scan in private storage after verifying the declared
    /// content type is one of the documented types (PDF/JPEG/PNG/WebP) AND that
    /// the actual bytes carry the matching signature. The declared type alone is
    /// never trusted, so a mislabelled payload is refused with no storage write.
    /// </summary>
    private async Task<Result<StoredFile>>
        SavePrivateCertificateAsync(
            IFormFile? file,
            CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        if (file.Length > MaximumCertificateBytes)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.TooLarge);
        }

        string contentType =
            file.ContentType?.Trim().ToLowerInvariant() ??
            string.Empty;

        await using var content =
            new MemoryStream();

        await file.CopyToAsync(
            content,
            cancellationToken);

        if (!HasMatchingCertificateSignature(
                contentType,
                content.GetBuffer().AsSpan(0, (int)content.Length)))
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        content.Position = 0;

        return await _fileStorage.SavePrivateAsync(
            content,
            contentType,
            file.Length,
            folder: CertificateStorage.Folder,
            cancellationToken);
    }

    /// <summary>
    /// Byte-signature check for the accepted certificate types; any other
    /// declared type has no allowed signature and is rejected.
    /// </summary>
    private static bool HasMatchingCertificateSignature(
        string contentType,
        ReadOnlySpan<byte> bytes) =>
        contentType switch
        {
            "application/pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
}
