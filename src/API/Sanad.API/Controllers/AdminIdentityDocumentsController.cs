using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;

namespace Sanad.API.Controllers;

public sealed record ReviewIdentityDocumentRequest(
    string Reason);

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin/identity-documents")]
public sealed class AdminIdentityDocumentsController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminIdentityDocumentsController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(PagedIdentityDocumentList),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentityDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] IdentityDocumentVerificationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetAdminIdentityDocumentsQuery(
                    page,
                    pageSize,
                    status),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpGet("{userId:guid}")]
    [ProducesResponseType(
        typeof(AdminIdentityDocumentDetail),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIdentityDocument(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetAdminIdentityDocumentQuery(
                    new UserId(userId)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpGet("{userId:guid}/front")]
    public async Task<IActionResult> DownloadFront(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await DownloadSide(
            userId,
            IdentityDocumentFileSide.Front,
            cancellationToken);
    }

    [HttpGet("{userId:guid}/back")]
    public async Task<IActionResult> DownloadBack(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await DownloadSide(
            userId,
            IdentityDocumentFileSide.Back,
            cancellationToken);
    }

    [HttpPost("{userId:guid}/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Verify(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new VerifyIdentityDocumentCommand(
                    new UserId(userId)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("{userId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(
        Guid userId,
        [FromBody] ReviewIdentityDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RejectIdentityDocumentCommand(
                    new UserId(userId),
                    request.Reason),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("{userId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        Guid userId,
        [FromBody] ReviewIdentityDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RevokeIdentityDocumentCommand(
                    new UserId(userId),
                    request.Reason),
                cancellationToken);

        return ToActionResult(
            result);
    }

    private async Task<IActionResult> DownloadSide(
        Guid userId,
        IdentityDocumentFileSide side,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetIdentityDocumentFileQuery(
                    new UserId(userId),
                    side),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(
                result);
        }

        return File(
            result.Value.Content,
            result.Value.ContentType,
            result.Value.FileName);
    }
}
