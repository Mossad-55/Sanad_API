using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Posts;

namespace Ahlena.API.Controllers;

/// <summary>
/// Authorized download of a Community author's avatar (the S3 approved photo
/// route, refund decision lock #32). Kept as its own controller so the
/// existing Community post/comment controllers are not churned; the policy is
/// the same Normal JWT the Community reads require.
/// <para>
/// The response carries the image bytes only. The stored Identity avatar key
/// is never returned on the wire, and an anonymous author is never served:
/// the anonymity rule is enforced inside the handler, so the existing
/// <c>authorId</c> fields are unchanged and anonymity stays byte-identical.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/community/authors")]
public sealed class CommunityAuthorPhotosController : ApiControllerBase
{
    private readonly ISender _sender;

    public CommunityAuthorPhotosController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{authorId:guid}/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuthorPhoto(
        Guid authorId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetCommunityAuthorPhotoQuery(new UserId(authorId)),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        Response.GetTypedHeaders().CacheControl =
            new Microsoft.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        Response.Headers.Pragma = "no-cache";

        // FileStreamResult disposes the stream after sending.
        return File(result.Value.Content, result.Value.ContentType);
    }
}
