using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.CheckIns;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyMedicationOperationalRead)]
[Route("api/v1/admin/elderly/check-ins")]
public sealed class AdminElderlyCheckInsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? elderlyId = null, [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, [FromQuery] bool? answer = null, CancellationToken ct = default)
        => await Send(new GetAdminElderlyCheckInsQuery(Actor(), AccountType(), CorrelationId(), page, pageSize, elderlyId, startDate, endDate, answer), ct);

    [HttpGet("{checkInId:guid}")]
    public async Task<IActionResult> Get(Guid checkInId, CancellationToken ct) => await Send(new GetAdminElderlyCheckInQuery(Actor(), AccountType(), CorrelationId(), checkInId), ct);

    [HttpGet("timeline/{elderlyId:guid}")]
    public async Task<IActionResult> Timeline(Guid elderlyId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken ct) => await Send(new GetAdminElderlyCheckInTimelineQuery(Actor(), AccountType(), CorrelationId(), elderlyId, startDate, endDate), ct);

    [HttpGet("aggregate")]
    public async Task<IActionResult> Aggregate([FromQuery] Guid? elderlyId = null, [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, CancellationToken ct = default) => await Send(new GetAdminElderlyCheckInAggregateQuery(Actor(), AccountType(), CorrelationId(), elderlyId, startDate, endDate), ct);

    private async Task<IActionResult> Send<T>(IQuery<T> query, CancellationToken ct) => ToActionResult(await sender.Send(query, ct));
    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    private string AccountType() => User.FindFirst(AuthClaimNames.AccountType)!.Value;
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : HttpContext.TraceIdentifier;
}
