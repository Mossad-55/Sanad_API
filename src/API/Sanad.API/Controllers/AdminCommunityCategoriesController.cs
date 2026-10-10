using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Community.Application.Categories;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CommunityModeration)]
[Route("api/v1/admin/community/categories")]
public sealed class AdminCommunityCategoriesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _)) return Unauthorized();
        var result = await sender.Send(new GetCommunityCategoriesAdminQuery(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminCategoryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _)) return Unauthorized();
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToActionResult(result);
    }

    [HttpPut("{categoryId:guid}")]
    [ProducesResponseType(typeof(AdminCategoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCategory(
        Guid categoryId,
        [FromBody] UpdateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _)) return Unauthorized();
        command = command with { Id = categoryId };
        var result = await sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{categoryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _)) return Unauthorized();
        var result = await sender.Send(new DeleteCategoryCommand(categoryId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }
}
