using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.HelpRequests;
namespace Sanad.API.Controllers;
[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/help-requests")]
public sealed class ElderlyHelpRequestsController(ISender sender) : ApiControllerBase
{
 [HttpPost] public async Task<IActionResult> Create([FromBody] CreateRequest r, [FromHeader(Name="Idempotency-Key")] string? key, CancellationToken ct) { if (!TryGetAuthenticatedUserId(out UserId id)) return Unauthorized(); return ToActionResult(await sender.Send(new CreateElderlyHelpRequestCommand(id,r.ActorKey,r.ActionKey,r.NeedKey,r.QualifierKey,r.CustomText,key ?? ""),ct)); }
 [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => ToActionResult(await sender.Send(new GetElderlyHelpRequestsQuery(Actor()),ct));
 [HttpGet("{requestId:guid}")] public async Task<IActionResult> Detail(Guid requestId,CancellationToken ct)=>ToActionResult(await sender.Send(new GetElderlyHelpRequestQuery(Actor(),requestId),ct));
 [HttpPost("{requestId:guid}/cancel")] public async Task<IActionResult> Cancel(Guid requestId,[FromBody] ReasonRequest r,CancellationToken ct)=>ToActionResult(await sender.Send(new CancelElderlyHelpRequestCommand(Actor(),requestId,r.Reason),ct));
 private UserId Actor()=>new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
 public sealed record CreateRequest(string ActorKey,string ActionKey,string NeedKey,string? QualifierKey,string? CustomText); public sealed record ReasonRequest(string? Reason);
}
