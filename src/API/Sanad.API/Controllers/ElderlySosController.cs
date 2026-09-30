using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Sos;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/sos")]
public sealed class ElderlySosController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId id)) return Unauthorized(); return ToActionResult(await sender.Send(new CreateElderlySosCommand(id, key ?? "", request.LocationConsentGranted, request.Latitude, request.Longitude), ct)); }
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => ToActionResult(await sender.Send(new GetElderlySosQuery(Actor()), ct));
    [HttpGet("{sosId:guid}")] public async Task<IActionResult> Detail(Guid sosId, CancellationToken ct) => ToActionResult(await sender.Send(new GetElderlySosDetailQuery(Actor(), sosId), ct));
    [HttpPost("{sosId:guid}/cancel")] public async Task<IActionResult> Cancel(Guid sosId, CancellationToken ct) => ToActionResult(await sender.Send(new CancelElderlySosCommand(Actor(), sosId), ct));
    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    public sealed record CreateRequest(bool LocationConsentGranted, decimal? Latitude, decimal? Longitude);
}
