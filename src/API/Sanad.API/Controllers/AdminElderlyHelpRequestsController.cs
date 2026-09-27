using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.HelpRequests;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
namespace Sanad.API.Controllers;
[Authorize(Policy = AuthorizationPolicies.ElderlyHelpRequestOperational)]
[Route("api/v1/admin/elderly/help-requests")]
public sealed class AdminElderlyHelpRequestsController(ISender sender) : ApiControllerBase
{
 [HttpGet] public async Task<IActionResult> List([FromQuery] ElderlyHelpRequestStatus? status=null,[FromQuery] Guid? elderlyId=null,[FromQuery] int page=1,[FromQuery] int pageSize=20,CancellationToken ct=default)=>await Send(new AdminListElderlyHelpRequestsQuery(Actor(),Account(),Correlation(),status,elderlyId,page,pageSize),ct);
 [HttpGet("{requestId:guid}")] public async Task<IActionResult> Detail(Guid requestId,CancellationToken ct)=>await Send(new AdminGetElderlyHelpRequestQuery(Actor(),Account(),Correlation(),requestId),ct);
 [HttpGet("{requestId:guid}/history")] public async Task<IActionResult> History(Guid requestId,CancellationToken ct)=>await Send(new AdminGetElderlyHelpRequestHistoryQuery(Actor(),Account(),Correlation(),requestId),ct);
 [HttpGet("aggregate")] public async Task<IActionResult> Aggregate(CancellationToken ct)=>await Send(new AdminGetElderlyHelpRequestAggregateQuery(Actor(),Account(),Correlation()),ct);
 [HttpPost("{requestId:guid}/{action}")] public async Task<IActionResult> Change(Guid requestId,ElderlyHelpRequestHistoryAction action,[FromBody] ReasonRequest r,CancellationToken ct)=>await Send(new ChangeElderlyHelpRequestStatusCommand(Actor(),requestId,action,r.Reason),ct);
 private async Task<IActionResult> Send<T>(Sanad.BuildingBlocks.Application.CQRS.IQuery<T> q,CancellationToken ct)=>ToActionResult(await sender.Send(q,ct));
 private async Task<IActionResult> Send<T>(Sanad.BuildingBlocks.Application.CQRS.ICommand<T> q,CancellationToken ct)=>ToActionResult(await sender.Send(q,ct));
 private UserId Actor()=>new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value)); private string Account()=>User.FindFirst(AuthClaimNames.AccountType)!.Value; private string Correlation()=>Request.Headers.TryGetValue("X-Correlation-ID",out var x)&&!string.IsNullOrWhiteSpace(x)?x.ToString():HttpContext.TraceIdentifier;
 public sealed record ReasonRequest(string? Reason);
}
