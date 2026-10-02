using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.API.Controllers;

public sealed record VerifyCareHomeDocumentRequest(int ExpectedVersion, DateOnly? ExpiryDate, bool ConfirmNonExpiring);
public sealed record RejectCareHomeDocumentRequest(int ExpectedVersion, string Reason);
public sealed record ReviewCareHomeRequest(int ExpectedVersion, CareHomeReviewAction Action, string? Reason);

[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes")]
public sealed class AdminCareHomesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CareHomeAdminApplicationsPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null,
        CancellationToken cancellationToken = default)
    {
        CareHomeStatus? parsedStatus = status is null ? null : (CareHomeStatus)status.Value;
        return ToActionResult(await sender.Send(
            new GetAdminCareHomeApplicationsQuery(page, pageSize, parsedStatus), cancellationToken));
    }

    [HttpGet("{careHomeId:guid}")]
    [ProducesResponseType(typeof(CareHomeAdminApplicationDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(Guid careHomeId, CancellationToken cancellationToken) =>
        ToActionResult(await sender.Send(new GetAdminCareHomeApplicationQuery(careHomeId), cancellationToken));

    [HttpGet("{careHomeId:guid}/documents/{documentId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadPrivateDocument(Guid careHomeId, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminCareHomeDocumentFileQuery(careHomeId, documentId), cancellationToken);
        if (result.IsFailure)
            return ToActionResult(result);
        PrivateFileContent content = result.Value;
        return File(content.Content, content.ContentType, enableRangeProcessing: false);
    }

    [HttpPost("{careHomeId:guid}/documents/{documentId:guid}/verify")]
    [ProducesResponseType(typeof(CareHomeAdminApplicationDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyDocument(
        Guid careHomeId,
        Guid documentId,
        [FromBody] VerifyCareHomeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(new ReviewCareHomeDocumentCommand(
            actorUserId, careHomeId, documentId, request.ExpectedVersion, true,
            request.ExpiryDate, request.ConfirmNonExpiring, null), cancellationToken));
    }

    [HttpPost("{careHomeId:guid}/documents/{documentId:guid}/reject")]
    [ProducesResponseType(typeof(CareHomeAdminApplicationDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectDocument(
        Guid careHomeId,
        Guid documentId,
        [FromBody] RejectCareHomeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(new ReviewCareHomeDocumentCommand(
            actorUserId, careHomeId, documentId, request.ExpectedVersion, false,
            null, false, request.Reason), cancellationToken));
    }

    [HttpPost("{careHomeId:guid}/review")]
    [ProducesResponseType(typeof(CareHomeAdminApplicationDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(
        Guid careHomeId,
        [FromBody] ReviewCareHomeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();
        return ToActionResult(await sender.Send(new ReviewCareHomeApplicationCommand(
            actorUserId, careHomeId, request.ExpectedVersion, request.Action, request.Reason), cancellationToken));
    }
}
