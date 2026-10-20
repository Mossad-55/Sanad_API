using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Identity.Application.Support;

namespace Sanad.API.Controllers;

[Route("api/v1/support")]
public sealed class SupportController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public SupportController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpGet("contact")]
    [ProducesResponseType(
        typeof(PublicSupportContactResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupportContact(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetPublicSupportContactQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("contact")]
    [ProducesResponseType(
        typeof(SupportRequestResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitSupportRequest(
        [FromBody] SubmitSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new SubmitSupportRequestCommand(
                    userId,
                    request.Subject,
                    request.Message),
                cancellationToken);

        return ToActionResult(result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("tickets")]
    [ProducesResponseType(
        typeof(MySupportTicketsPage),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetMySupportTicketsQuery(
                    userId,
                    page,
                    pageSize),
                cancellationToken);

        return ToActionResult(result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("tickets/{id:guid}")]
    [ProducesResponseType(
        typeof(MySupportTicketDetail),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTicketById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetMySupportTicketByIdQuery(
                    userId,
                    new SupportTicketId(id)),
                cancellationToken);

        return ToActionResult(result);
    }
}
