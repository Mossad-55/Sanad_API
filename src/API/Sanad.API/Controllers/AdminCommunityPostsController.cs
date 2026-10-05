using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Community.Application.Posts;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CommunityModeration)]
[Route("api/v1/admin/community/posts")]
public sealed class AdminCommunityPostsController(ISender sender) : ApiControllerBase
{
    [HttpPost("{postId:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(new PublishCommunityPostCommand(postId, moderatorId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("{postId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(new RejectCommunityPostCommand(postId, moderatorId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }
}
