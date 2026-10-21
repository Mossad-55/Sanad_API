using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Families.Application.Families;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/home")]
public sealed class FamilyHomeController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// The shared editorial Family home read. Access is authorized by the policy
    /// first and then resolved server-side to a non-deleted family the caller
    /// owns or belongs to; an unresolvable family is a 404 through the existing
    /// Families error conventions, exactly like the other Family reads.
    /// </summary>
    [HttpGet("sections")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilyHomeSectionPublicItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSections(CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        Result<Guid> access = await sender.Send(
            new GetFamilyHomeAccessQuery(userId.Value), cancellationToken);
        if (access.IsFailure)
            return ToActionResult(access);

        return this.ToFamilyHomeActionResult(await sender.Send(
            new GetPublishedFamilyHomeSectionsQuery(), cancellationToken));
    }
}
