using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Bookings;

namespace Sanad.API.Controllers;

public sealed record CaregiverAdminDetailResponse(
    CaregiverProfileResponse Profile,
    CaregiverCancellationSummaryResponse? Cancellations);

/// <summary>
/// E07 review-note body. There is deliberately no author and no timestamp
/// member: both are taken from the authenticated request (the actor claim and
/// the server clock), never from the client.
/// </summary>
public sealed record AddCaregiverReviewNoteRequest(string? Text);

[Authorize(Policy = AuthorizationPolicies.CaregiverReviewAdmin)]
[Route("api/v1/admin/caregivers")]
public sealed class AdminCaregiversController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminCaregiversController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyCertificate(
        Guid caregiverId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new VerifyCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RejectCertificate(
        Guid caregiverId,
        Guid certificateId,
        [FromBody] ReviewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RejectCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId),
                    request.Reason),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/certificates/{certificateId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeCertificate(
        Guid caregiverId,
        Guid certificateId,
        [FromBody] ReviewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RevokeCertificateCommand(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}/certificates/{certificateId:guid}/file")]
    public async Task<IActionResult> DownloadCertificateFile(
        Guid caregiverId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetCertificateFileQuery(
                    new CaregiverId(caregiverId),
                    new CaregiverCertificateId(certificateId)),
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

    [HttpGet]
    [ProducesResponseType(
       typeof(PagedCaregiverList),
       StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCaregivers(
       [FromQuery] int page = 1,
       [FromQuery] int pageSize = 10,
       [FromQuery] CaregiverStatus? status = null,
       [FromQuery] CaregiverType? type = null,
       CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCaregiversAdminQuery(
            page,
            pageSize,
            status,
            type),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}")]
    [ProducesResponseType(
       typeof(CaregiverAdminDetailResponse),
       StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCaregiverDetail(
       Guid caregiverId,
       CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetCaregiverAdminDetailQuery(
                    new CaregiverId(caregiverId)),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        // Suspension-decision support data. A cancellations failure never
        // fails the detail request — the field is simply null.
        var cancellations =
            await _sender.Send(
                new GetCaregiverCancellationSummaryQuery(
                    new CaregiverId(caregiverId)),
                cancellationToken);

        return Ok(new CaregiverAdminDetailResponse(
            result.Value,
            cancellations.IsSuccess ? cancellations.Value : null));
    }

    [HttpPost("{caregiverId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ApproveCaregiverCommand(
                    new CaregiverId(caregiverId),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RejectCaregiverApplicationCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/request-correction")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestCorrection(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RequestCaregiverCorrectionCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(
        Guid caregiverId,
        [FromBody] ReviewCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SuspendCaregiverCommand(
                    new CaregiverId(caregiverId),
                    request.Reason,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ReactivateCaregiverCommand(
                    new CaregiverId(caregiverId),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// E07 append-only review notes. One caregiver's administrative notes,
    /// newest first with the note id as a stable tiebreak; paging is clamped
    /// (page at least 1, pageSize defaults to 20 and is capped at 100) and the
    /// count always covers the caregiver's full note set. An unknown caregiver
    /// is 404 and a caregiver with no notes is an empty page, never an error.
    /// The projection carries the note text, its author and its creation
    /// instant and nothing about the caregiver's identity or medical data.
    /// </summary>
    [HttpGet("{caregiverId:guid}/notes")]
    [ProducesResponseType(
       typeof(AdminCaregiverReviewNotePage),
       StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewNotes(
       Guid caregiverId,
       [FromQuery] int page = 1,
       [FromQuery] int pageSize = 20,
       CancellationToken cancellationToken = default) =>
        ToReviewNoteActionResult(
            await _sender.Send(
                new ListAdminCaregiverReviewNotesQuery(
                    new CaregiverId(caregiverId),
                    page,
                    pageSize),
                cancellationToken));

    /// <summary>
    /// E07 appends exactly one immutable note and returns 201 with the created
    /// body so the caller can read back exactly what was stored. The author is
    /// the authenticated actor and the instant is this server's UTC clock; the
    /// note text is trimmed and stored verbatim otherwise (no masking, no
    /// truncation, no markdown). An unknown caregiver is 404, an empty,
    /// whitespace-only or over-2000-character text is 400. Creating a note
    /// changes no caregiver state and emits no notification or domain event.
    /// </summary>
    [HttpPost("{caregiverId:guid}/notes")]
    [ProducesResponseType(
       typeof(AdminCaregiverReviewNoteItem),
       StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddReviewNote(
       Guid caregiverId,
       [FromBody] AddCaregiverReviewNoteRequest request,
       CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId authorUserId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new AddAdminCaregiverReviewNoteCommand(
                    new CaregiverId(caregiverId),
                    authorUserId,
                    request.Text,
                    DateTime.UtcNow),
                cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToReviewNoteActionResult(result);
    }

    /// <summary>
    /// E07 pins its two review-note status codes here instead of editing the
    /// shared <see cref="ResultProblemDetailsMapper"/> (unknown codes there
    /// default to 400): a missing caregiver must be a real 404 while a bad text
    /// stays a 400. The response keeps the mapper's <c>code</c> and
    /// <c>traceId</c> extensions so a client can key on the code. No 409 and no
    /// 5xx path is reachable from request input — nothing in this phase is
    /// concurrent or mutable.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> ReviewNoteStatusCodesByErrorCode =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Caregivers.ReviewNote.CaregiverNotFound"] = StatusCodes.Status404NotFound,
            ["Caregivers.ReviewNote.InvalidText"] = StatusCodes.Status400BadRequest
        };

    private IActionResult ToReviewNoteActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(result.Error, HttpContext);

        if (ReviewNoteStatusCodesByErrorCode.TryGetValue(
                result.Error.Code,
                out int statusCode))
        {
            problemDetails.Type = $"https://httpstatuses.com/{statusCode}";
            problemDetails.Status = statusCode;
            problemDetails.Title = statusCode switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                _ => "Bad Request"
            };
            problemDetails.Detail = statusCode switch
            {
                StatusCodes.Status404NotFound =>
                    "The requested resource was not found.",
                _ => "The request could not be completed."
            };
        }

        return StatusCode(
            problemDetails.Status ??
            StatusCodes.Status500InternalServerError,
            problemDetails);
    }
}
