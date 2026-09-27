using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Sos;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlySosOperational)]
[Route("api/v1/admin/elderly/sos")]
public sealed class AdminElderlySosController(ISender sender) : ApiControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] ElderlySosStatus? status = null, [FromQuery] Guid? elderlyId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => await Send(new AdminListElderlySosQuery(Actor(), Account(), Correlation(), status, elderlyId, page, pageSize), ct);
    [HttpGet("{sosId:guid}")] public async Task<IActionResult> Detail(Guid sosId, CancellationToken ct) => await Send(new AdminGetElderlySosQuery(Actor(), Account(), Correlation(), sosId), ct);
    [HttpGet("{sosId:guid}/history")] public async Task<IActionResult> History(Guid sosId, CancellationToken ct) => await Send(new AdminGetElderlySosHistoryQuery(Actor(), Account(), Correlation(), sosId), ct);
    [HttpPost("{sosId:guid}/status")] public async Task<IActionResult> Status(Guid sosId, [FromBody] StatusRequest request, CancellationToken ct) => await Send(new ChangeElderlySosStatusCommand(Actor(), Account(), Correlation(), sosId, request.Action), ct);
    private async Task<IActionResult> Send<T>(Sanad.BuildingBlocks.Application.CQRS.IQuery<T> query, CancellationToken ct) => ToActionResult(await sender.Send(query, ct));
    private async Task<IActionResult> Send<T>(Sanad.BuildingBlocks.Application.CQRS.ICommand<T> command, CancellationToken ct) => ToActionResult(await sender.Send(command, ct));
    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    private string Account() => User.FindFirst(AuthClaimNames.AccountType)!.Value;
    private string Correlation() => Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : HttpContext.TraceIdentifier;
    public sealed record StatusRequest(ElderlySosHistoryAction Action);
}
