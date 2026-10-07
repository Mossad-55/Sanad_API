using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.MedicalAccess;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.API.Controllers;

[Authorize(Policy = "FamilyAccess")]
[Route("api/v1/family/dependents")]
public sealed class DependentMedicalAccessGrantsController : ApiControllerBase
{
    private readonly ISender _sender;

    public DependentMedicalAccessGrantsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{dependentId:guid}/medical-access-grants")]
    [ProducesResponseType(
        typeof(MedicalAccessGrantResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateMedicalAccessGrant(
        Guid dependentId,
        [FromBody] CreateMedicalAccessGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<MedicalAccessGrantType>(request.GrantType, true, out var grantType) ||
            !Enum.IsDefined(grantType))
        {
            return BadRequest(new { code = "MedicalAccess.InvalidGrant", message = "Grant type is invalid." });
        }

        var command = new CreateMedicalAccessGrantCommand(
            new ElderlyId(dependentId), userId, new UserId(request.GranteeUserId), grantType,
            request.CanViewRecords, request.CanEditRecords, request.CanShareWithOthers,
            request.ExpiresOnUtc, request.Notes);

        var result = await _sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        var created = result.Value;
        var response = new MedicalAccessGrantResponse(
            created.Id.Value, created.DependentId.Value, created.GranteeUserId.Value,
            created.GrantType.ToString(),
            created.CanViewRecords, created.CanEditRecords, created.CanShareWithOthers,
            created.GrantedOnUtc, created.ExpiresOnUtc, created.Notes, created.RevokedOnUtc, created.IsActive);

        return CreatedAtAction(
            nameof(GetMedicalAccessGrants),
            new { dependentId },
            response);
    }

    [HttpGet("{dependentId:guid}/medical-access-grants")]
    [ProducesResponseType(
        typeof(MedicalAccessGrantResponse[]),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedicalAccessGrants(
        Guid dependentId,
        [FromQuery] Guid? grantId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetMedicalAccessGrantsQuery(dependentId, userId, grantId);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{dependentId:guid}/medical-access-grants/{grantId:guid}")]
    [ProducesResponseType(
        typeof(MedicalAccessGrantResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedicalAccessGrant(
        Guid dependentId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetMedicalAccessGrantsQuery(dependentId, userId, grantId);
        var result = await _sender.Send(query, cancellationToken);
        if (result.IsFailure) return ToActionResult(result);
        return result.Value.FirstOrDefault() is { } grant ? Ok(grant) : NotFound();
    }

    [HttpDelete("{dependentId:guid}/medical-access-grants/{grantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMedicalAccessGrant(
        Guid dependentId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var command = new RevokeMedicalAccessGrantCommand(
            new ElderlyId(dependentId), new MedicalAccessGrantId(grantId), userId);
        var result = await _sender.Send(command, cancellationToken);
        if (result.IsFailure) return ToActionResult(result);
        return NoContent();
    }
}
