using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.Modules.Caregivers.Application.Lookups;

namespace Sanad.API.Controllers;

[Route("api/v1")]
public sealed class LookupsController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public LookupsController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpGet("lookups/services")]
    [ProducesResponseType(
        typeof(IReadOnlyList<ServicePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveServices(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetActiveServicesQuery(),
                cancellationToken);

        return ToActionResult(
            result);
    }
}