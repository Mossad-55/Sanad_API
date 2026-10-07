using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.MedicationTasks;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregivers")]
public sealed class CaregiverMedicationTasksController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverMedicationTasksController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{caregiverId:guid}/medication-tasks")]
    [ProducesResponseType(
        typeof(CaregiverMedicationTaskResponse[]),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedicationTasks(
        Guid caregiverId,
        [FromQuery] MedicationTaskStatus? status = null,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCaregiverMedicationTasksQuery(
            caregiverId,
            userId,
            status,
            startDate,
            endDate);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/medication-tasks/{taskId}/administer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AdministerMedicationTask(
        Guid caregiverId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new AdministerCaregiverMedicationTaskCommand(
            caregiverId,
            new MedicationDoseLogId(taskId),
            userId,
            null);
        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{caregiverId:guid}/medication-tasks/{taskId}/skip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SkipMedicationTask(
        Guid caregiverId,
        Guid taskId,
        [FromBody] SkipCaregiverMedicationTaskRequest? body,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new SkipCaregiverMedicationTaskCommand(
            caregiverId,
            new MedicationDoseLogId(taskId),
            userId,
            body?.Reason ?? string.Empty);
        var result = await _sender.Send(command, cancellationToken);
        return ToActionResult(result);
    }
}
