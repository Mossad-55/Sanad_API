using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Uploads;

namespace Sanad.API.Controllers;

[Authorize]
[Route("api/v1/community/uploads")]
public sealed class CommunityUploadsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IAuthorizationService _authorizationService;

    public CommunityUploadsController(
        ISender sender,
        IAuthorizationService authorizationService)
    {
        _sender = sender;
        _authorizationService = authorizationService;
    }

    [HttpPost("images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2_097_152)]
    [ProducesResponseType(
        typeof(CommunityImageUploadResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadImage(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        if (file is null)
        {
            return BadRequest(new { code = "Community.Image.Invalid", message = "An image file is required." });
        }

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        var command = new UploadCommunityImageCommand(
            userId,
            content,
            file.ContentType,
            file.Length);

        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("images/{imageId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadImage(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        AuthorizationResult moderation = await _authorizationService.AuthorizeAsync(
            User,
            null,
            AuthorizationPolicies.CommunityModeration);

        var result = await _sender.Send(
            new GetCommunityImageFileQuery(imageId, userId, moderation.Succeeded),
            cancellationToken);

        if (!result.IsSuccess) return ToActionResult(result);

        Response.GetTypedHeaders().CacheControl =
            new Microsoft.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        Response.Headers.Pragma = "no-cache";

        // FileStreamResult disposes the stream after sending.
        return File(result.Value.Content, result.Value.ContentType);
    }
}
