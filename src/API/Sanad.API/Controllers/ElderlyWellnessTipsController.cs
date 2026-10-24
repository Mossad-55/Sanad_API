using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.Wellness;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/wellness-tips")]
public sealed class ElderlyWellnessTipsController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Paged published-only feed. <c>page</c> is clamped to at least 1 and
    /// <c>pageSize</c> is clamped to 1–100, so paging values never produce a
    /// business 400; a non-integer <c>page</c>/<c>pageSize</c> or an undefined
    /// value is still rejected by model binding. Ordered by publication or
    /// (for never-published rows) update time, then id.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(WellnessTipPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Feed([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => ToActionResult(await sender.Send(new ListWellnessTipsQuery(null, null, null, true, page, pageSize), ct));

    /// <summary>
    /// Published-only detail. A missing id, a draft or an archived tip is
    /// <c>404 Cms.WellnessTip.NotPublished</c> (ProblemDetails), the same
    /// answer the feed's published-only filter enforces.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WellnessTipResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new GetWellnessTipQuery(id, true), ct));
}
