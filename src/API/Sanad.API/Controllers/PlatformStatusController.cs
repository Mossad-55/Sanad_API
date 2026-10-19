using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Controllers;

/// <summary>
/// E04 anonymous, sanitized public business status. Returns only the public
/// state, the approved bilingual maintenance message, and the currently
/// available supported payment methods. It never exposes internal reasons,
/// gateway configuration, actor identity, audit history or the concurrency
/// version. An unreadable control record fails closed with a generic 503
/// instead of assuming Operational.
/// </summary>
[Route("api/v1/platform")]
public sealed class PlatformStatusController(ISender sender) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("status")]
    [ProducesResponseType(typeof(PublicPlatformStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => ToActionResult(await sender.Send(new GetPublicPlatformStatusQuery(), ct));
}
