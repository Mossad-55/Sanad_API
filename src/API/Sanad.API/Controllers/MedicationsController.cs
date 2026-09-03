using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Medications;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/dependents/{dependentId:guid}/medications")]
public sealed class MedicationsController : ApiControllerBase
{
    private readonly ISender _sender;

    public MedicationsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMedication(
        Guid dependentId,
        [FromBody] AddMedicationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new AddMedicationCommand(
            userId,
            new ElderlyId(dependentId),
            request.Name,
            request.Dosage,
            request.DoseUnit,
            request.DoseQuantity,
            request.DoseTimes,
            request.StartDate,
            request.EndDate,
            request.Instructions,
            request.StockQuantity,
            request.LowStockThreshold),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<MedicationResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMedications(
        Guid dependentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new ListMedicationsQuery(userId, new ElderlyId(dependentId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(
        typeof(MedicationDashboardResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(
        Guid dependentId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        DateOnly targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await _sender.Send(
            new GetMedicationDashboardQuery(userId, new ElderlyId(dependentId), targetDate),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{medicationId:guid}")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedication(
        Guid dependentId,
        Guid medicationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetMedicationByIdQuery(userId, new ElderlyId(dependentId), new MedicationId(medicationId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{medicationId:guid}")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedication(
        Guid dependentId,
        Guid medicationId,
        [FromBody] UpdateMedicationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new UpdateMedicationCommand(
            userId,
            new ElderlyId(dependentId),
            new MedicationId(medicationId),
            request.Name,
            request.Dosage,
            request.DoseUnit,
            request.DoseQuantity,
            request.DoseTimes,
            request.StartDate,
            request.EndDate,
            request.Instructions), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{medicationId:guid}/stock")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStock(
        Guid dependentId,
        Guid medicationId,
        [FromBody] UpdateMedicationStockRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }
        var result = await _sender.Send(new UpdateMedicationStockCommand(
            userId,
            new ElderlyId(dependentId),
            new MedicationId(medicationId),
            request.StockQuantity,
            request.LowStockThreshold), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{medicationId:guid}/pause")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> PauseMedication(
        Guid dependentId,
        Guid medicationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new PauseMedicationCommand(userId, new ElderlyId(dependentId), new MedicationId(medicationId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{medicationId:guid}/resume")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ResumeMedication(
        Guid dependentId,
        Guid medicationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new ResumeMedicationCommand(userId, new ElderlyId(dependentId), new MedicationId(medicationId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{medicationId:guid}/discontinue")]
    [ProducesResponseType(
        typeof(MedicationResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> DiscontinueMedication(
        Guid dependentId,
        Guid medicationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new DiscontinueMedicationCommand(userId, new ElderlyId(dependentId), new MedicationId(medicationId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{medicationId:guid}/doses/take")]
    [ProducesResponseType(
        typeof(MedicationDoseResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordDoseTaken(
        Guid dependentId,
        Guid medicationId,
        [FromBody] RecordDoseTakenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new RecordDoseTakenCommand(
            userId,
            new ElderlyId(dependentId),
            new MedicationId(medicationId),
            request.ScheduledDate,
            request.ScheduledTime,
            request.Notes,
            DateTime.UtcNow), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{medicationId:guid}/doses/skip")]
    [ProducesResponseType(
        typeof(MedicationDoseResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordDoseSkipped(
        Guid dependentId,
        Guid medicationId,
        [FromBody] RecordDoseSkippedRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new RecordDoseSkippedCommand(
            userId,
            new ElderlyId(dependentId),
            new MedicationId(medicationId),
            request.ScheduledDate,
            request.ScheduledTime,
            request.Reason,
            DateTime.UtcNow), cancellationToken);

        return ToActionResult(result);
    }
}