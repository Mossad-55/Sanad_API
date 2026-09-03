using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Notes;
using Sanad.Modules.Families.Domain.Notes;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/dependents/{dependentId:guid}/notes")]
public sealed class ElderlyNotesController : ApiControllerBase
{
    private readonly ISender _sender;

    public ElderlyNotesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(ElderlyNoteResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> AddNote(
        Guid dependentId,
        [FromBody] AddElderlyNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new AddElderlyNoteCommand(
            userId,
            new ElderlyId(dependentId),
            request.Title,
            request.Description,
            request.Category,
            request.Priority), cancellationToken);

        if (result.IsFailure) return ToActionResult(result);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<ElderlyNoteResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListNotes(
        Guid dependentId,
        [FromQuery] NoteCategory? category,
        [FromQuery] NotePriority? priority,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new ListElderlyNotesQuery(
            userId,
            new ElderlyId(dependentId),
            category,
            priority), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{noteId:guid}")]
    [ProducesResponseType(
        typeof(ElderlyNoteResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNote(
        Guid dependentId,
        Guid noteId,
        [FromBody] UpdateElderlyNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new UpdateElderlyNoteCommand(
            userId,
            new ElderlyId(dependentId),
            new ElderlyNoteId(noteId),
            request.Title,
            request.Description,
            request.Category,
            request.Priority), cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("{noteId:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteNote(
        Guid dependentId,
        Guid noteId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new DeleteElderlyNoteCommand(
            userId,
            new ElderlyId(dependentId),
            new ElderlyNoteId(noteId)), cancellationToken);

        return ToActionResult(result);
    }
}