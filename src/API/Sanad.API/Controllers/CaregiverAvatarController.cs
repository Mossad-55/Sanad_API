using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;

namespace Sanad.API.Controllers;

/// <summary>
/// Authorized download of a caregiver avatar (S3 approved photo route, refund
/// decision lock #32). Kept as its own controller to avoid churn in
/// <see cref="CaregiversDiscoveryController"/>; the route and policy match the
/// discovery reads.
/// <para>
/// The response carries the image bytes only. The stored Identity
/// <c>avatar_url</c> is a private storage key and is never returned on the
/// wire, and identity/medical media is never served here — only the avatar the
/// caregiver chose. A non-discoverable caregiver (inactive or hidden by its
/// own <c>show_profile</c> preference) and a caregiver with no avatar are both
/// a generic 404.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/caregivers")]
public sealed class CaregiverAvatarController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverAvatarController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{caregiverId:guid}/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCaregiverPhoto(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetCaregiverPhotoQuery(new CaregiverId(caregiverId)),
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
