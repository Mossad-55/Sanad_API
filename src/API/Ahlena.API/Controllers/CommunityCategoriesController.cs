using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.Modules.Community.Application.Categories;

namespace Ahlena.API.Controllers;

[Authorize]
[Route("api/v1/community")]
public sealed class CommunityCategoriesController(ISender sender) : ApiControllerBase
{
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveCategories(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _)) return Unauthorized();
        var result = await sender.Send(new GetActiveCategoriesQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
