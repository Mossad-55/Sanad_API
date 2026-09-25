using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Families.Application.Medications;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyMedicationOperationalRead)]
[Route("api/v1/admin/elderly/medications")]
public sealed class AdminElderlyMedicationsController : ApiControllerBase
{
    private readonly ISender _sender;
    public AdminElderlyMedicationsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? dependentId = null, [FromQuery] MedicationStatus? status = null, [FromQuery] string? search = null, CancellationToken ct = default)
        => await Send(new GetAdminMedicationsQuery(Actor(), AccountType(), CorrelationId(), page, pageSize, dependentId, status, search), ct);

    [HttpGet("{medicationId:guid}")]
    public async Task<IActionResult> Get(Guid medicationId, CancellationToken ct)
        => await Send(new GetAdminMedicationQuery(Actor(), AccountType(), CorrelationId(), new MedicationId(medicationId)), ct);

    [HttpGet("{medicationId:guid}/doses")]
    public async Task<IActionResult> Doses(Guid medicationId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, [FromQuery] DoseStatus? status = null, CancellationToken ct = default)
        => await Send(new GetAdminMedicationDoseTimelineQuery(Actor(), AccountType(), CorrelationId(), new MedicationId(medicationId), startDate, endDate, status), ct);

    [HttpGet("adherence")]
    public async Task<IActionResult> Adherence([FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, [FromQuery] Guid? dependentId = null, CancellationToken ct = default)
        => await Send(new GetAdminMedicationAdherenceQuery(Actor(), AccountType(), CorrelationId(), startDate, endDate, dependentId), ct);

    private async Task<IActionResult> Send<T>(Sanad.BuildingBlocks.Application.CQRS.IQuery<T> query, CancellationToken ct)
    {
        var result = await _sender.Send(query, ct);
        return ToActionResult(result);
    }

    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    private string AccountType() => User.FindFirst(AuthClaimNames.AccountType)!.Value;
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : HttpContext.TraceIdentifier;
}
