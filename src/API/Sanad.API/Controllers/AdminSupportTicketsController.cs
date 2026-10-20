using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Support;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SupportInboxAdmin)]
[Route("api/v1/admin/support-tickets")]
public sealed class AdminSupportTicketsController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminSupportTicketsController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(AdminSupportTicketsPage),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTickets(
        [FromQuery] int? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetAdminSupportTicketsQuery(status, page, pageSize),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(AdminSupportTicketDetail),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTicketById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetAdminSupportTicketByIdQuery(
                    new SupportTicketId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/status")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        [FromBody] ChangeSupportTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(
                typeof(SupportTicketStatus),
                request.Status))
        {
            return BadRequest();
        }

        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new ChangeSupportTicketStatusCommand(
                    new SupportTicketId(id),
                    (SupportTicketStatus)request.Status,
                    userId),
                cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("{id:guid}/replies")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(SupportTicketReplyDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reply(
        Guid id,
        [FromBody] ReplyToSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new ReplyToSupportTicketCommand(
                    new SupportTicketId(id),
                    userId,
                    request.Body),
                cancellationToken);

        return ToActionResult(result);
    }
}