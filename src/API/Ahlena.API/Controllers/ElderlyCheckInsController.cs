using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.CheckIns;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/check-ins")]
public sealed class ElderlyCheckInsController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        return ToActionResult(await sender.Send(new SubmitElderlyCheckInCommand(actor, request.Answer), ct));
    }

    public sealed record SubmitRequest(bool Answer);
}
