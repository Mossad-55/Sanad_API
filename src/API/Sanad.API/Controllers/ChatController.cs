using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Controllers;

/// <summary>
/// Chat app routes (endpoints 1, 2, and 10 of the frozen contract). Lane B
/// adds its own endpoints (3-4, 8, 11) to this controller later. All routes
/// resolve the participant from the JWT; unknown or foreign conversations
/// answer with a privacy-preserving 404 that never reveals why.
/// </summary>
[Authorize]
[Route("api/v1/chat")]
public sealed class ChatController : ApiControllerBase
{
    private readonly ISender _sender;

    public ChatController(ISender sender) => _sender = sender;

    /// <summary>Endpoint 1 — conversation list, last activity first.</summary>
    [HttpGet("conversations")]
    [ProducesResponseType(
        typeof(PagedResult<ConversationListItemResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetConversationsQuery(userId, page, pageSize),
            cancellationToken);

        return ToConversationActionResult(result);
    }

    /// <summary>Endpoint 2 — open-or-get (200, never a duplicate).</summary>
    [HttpPost("conversations")]
    [ProducesResponseType(
        typeof(OpenConversationResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OpenConversation(
        [FromBody] OpenConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        BookingId? bookingId = request.BookingId is { } value
            ? new BookingId(value)
            : null;

        var result = await _sender.Send(
            new OpenConversationCommand(
                userId,
                new UserId(request.RecipientUserId),
                bookingId),
            cancellationToken);

        return ToConversationActionResult(result);
    }

    /// <summary>
    /// Endpoint 10 — hide for the deleter only (204, idempotent).
    /// </summary>
    [HttpDelete("conversations/{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HideConversation(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new HideConversationCommand(
                userId,
                new ConversationId(conversationId)),
            cancellationToken);

        return ToConversationActionResult(result);
    }

    private IActionResult ToConversationActionResult(Result result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : ToActionResult(result);

    private IActionResult ToConversationActionResult<T>(Result<T> result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : ToActionResult(result);

    private static bool IsPrivacyFailure(Result result) =>
        result.IsFailure &&
        result.Error.Code == ConversationErrors.NotFound.Code;

    /// <summary>
    /// Chat has exactly one failure today and it is always a privacy 404,
    /// so the status/detail pair is forced here instead of editing the
    /// shared ResultProblemDetailsMapper (which parallel lanes also touch).
    /// The detail is the generic 404 text: the caller learns nothing about
    /// whether the conversation exists, who owns it, or why it was denied.
    /// </summary>
    private IActionResult PrivacyNotFound(Error error)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = "https://httpstatuses.com/404";
        problemDetails.Title = "Not Found";
        problemDetails.Status = StatusCodes.Status404NotFound;
        problemDetails.Detail = "The requested resource was not found.";

        return StatusCode(
            StatusCodes.Status404NotFound,
            problemDetails);
    }
}
