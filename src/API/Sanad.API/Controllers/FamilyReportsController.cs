using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Reports;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/reports")]
public sealed class FamilyReportsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public FamilyReportsController(ISender sender, IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedFamilyReportsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReports(
        [FromQuery] string? type,
        [FromQuery] Guid? elderlyId = null,
        [FromQuery] Guid? bookingId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetFamilyReportsQuery(
                userId,
                type,
                elderlyId.HasValue ? new ElderlyId(elderlyId.Value) : null,
                bookingId.HasValue ? new BookingId(bookingId.Value) : null,
                page,
                pageSize),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ToFamilyReportActionResult(result.Error);
    }

    [HttpGet("{reportId:guid}/photo")]
    public async Task<IActionResult> ReadMedicalReportPhoto(Guid reportId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new GetMedicalReportPhotoQuery(userId, new MedicalReportId(reportId), false), cancellationToken);
        if (result.IsFailure) return ToFamilyReportActionResult(result.Error);
        var file = await _fileStorage.OpenReadAsync(result.Value.Key, cancellationToken);
        if (file.IsFailure) return NotFound();
        return File(file.Value.Content, file.Value.ContentType);
    }

    private IActionResult ToVisitReportActionResult(Error error)
    {
        ProblemDetails problemDetails = ResultProblemDetailsMapper.Create(error, HttpContext);
        int statusCode = error.Code switch
        {
            "Reports.Visit.InvalidType" => StatusCodes.Status400BadRequest,
            "Reports.Visit.AccessDenied" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        problemDetails.Status = statusCode;
        return StatusCode(statusCode, problemDetails);
    }

    private IActionResult ToFamilyReportActionResult(Error error)
    {
        var details = ResultProblemDetailsMapper.Create(error, HttpContext);
        details.Status = error.Code.EndsWith("AccessDenied", StringComparison.Ordinal)
            ? 403
            : error.Code.EndsWith("PhotoNotFound", StringComparison.Ordinal)
                ? 404
                : 400;
        return StatusCode(details.Status.Value, details);
    }
}
