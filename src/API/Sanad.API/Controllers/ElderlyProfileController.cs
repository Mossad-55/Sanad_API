using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Elderlies;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/profile")]
public sealed class ElderlyProfileController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ElderlyOwnProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new GetOwnElderlyProfileQuery(userId),
            cancellationToken));
    }

    [HttpGet("photo")]
    public async Task<IActionResult> GetPhoto(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        var result = await sender.Send(
            new GetOwnElderlyProfilePhotoQuery(userId),
            cancellationToken);
        if (result.IsFailure)
            return ToActionResult(result);

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
