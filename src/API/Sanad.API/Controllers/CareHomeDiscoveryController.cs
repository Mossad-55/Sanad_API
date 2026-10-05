using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.Modules.CareHomes.Application.Discovery;

namespace Sanad.API.Controllers;

[AllowAnonymous]
[Route("api/v1/care-homes/discovery")]
public sealed class CareHomeDiscoveryController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CareHomeDiscoveryPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
        => ToActionResult(await sender.Send(new GetCareHomeDiscoveryQuery(page, pageSize), cancellationToken));

    [HttpGet("{careHomeId:guid}")]
    [ProducesResponseType(typeof(CareHomeDiscoveryDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid careHomeId, CancellationToken cancellationToken)
        => ToActionResult(await sender.Send(new GetCareHomeDiscoveryDetailQuery(careHomeId), cancellationToken));
}
