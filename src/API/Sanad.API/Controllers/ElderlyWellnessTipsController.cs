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
    [HttpGet]
    public async Task<IActionResult> Feed([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => ToActionResult(await sender.Send(new ListWellnessTipsQuery(null, null, null, true, page, pageSize), ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new GetWellnessTipQuery(id, true), ct));
}
