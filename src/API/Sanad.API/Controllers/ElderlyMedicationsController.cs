using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Medications;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/medications")]
public sealed class ElderlyMedicationsController : ApiControllerBase
{
    private readonly ISender _sender;

    public ElderlyMedicationsController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MedicationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await _sender.Send(
            new ListOwnMedicationsQuery(userId), cancellationToken));
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(MedicationDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        if (!date.HasValue)
            return BadRequest("date is required in YYYY-MM-DD format (Elderly profile-local date).");

        return ToActionResult(await _sender.Send(
            new GetOwnMedicationDashboardQuery(userId, date.Value), cancellationToken));
    }

    [HttpPost("{medicationId:guid}/doses/take")]
    [ProducesResponseType(typeof(MedicationDoseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TakeDose(
        Guid medicationId,
        [FromBody] RecordDoseTakenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await _sender.Send(new TakeOwnMedicationDoseCommand(
            userId,
            new MedicationId(medicationId),
            request.ScheduledDate,
            request.ScheduledTime,
            request.Notes,
            DateTime.UtcNow), cancellationToken));
    }
}
