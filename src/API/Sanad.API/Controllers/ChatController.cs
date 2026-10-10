using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Controllers;

/// <summary>
/// Chat app routes (endpoints 1, 2, 5, 6, and 10 of the frozen contract).
/// Lane B adds its own endpoints (3-4, 8, 11) to this controller later. All
/// routes resolve the participant from the JWT; unknown or foreign
/// conversations, attachments, and files answer with a privacy-preserving
/// 404 that never reveals why.
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

        return ToChatActionResult(result);
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

        return ToChatActionResult(result);
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

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 5 — upload a conversation attachment (multipart). The
    /// conversation must exist and the JWT user must be a participant;
    /// unknown and foreign conversations answer with the same privacy 404
    /// as endpoints 1/2/10. Validation failures (invalid/oversize/unsupported
    /// type) answer 400 with the specific Chat.Attachment.* code — the
    /// uploader is entitled to know why their own file failed.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52_428_800)]
    [ProducesResponseType(
        typeof(AttachmentUploadResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAttachment(
        Guid conversationId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        if (file is null)
        {
            return BadRequestWithCode(AttachmentErrors.Invalid.Code);
        }

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        var result = await _sender.Send(
            new UploadAttachmentCommand(
                userId,
                new ConversationId(conversationId),
                file.FileName,
                file.ContentType,
                file.Length,
                content),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ToChatActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Endpoint 6 — participant-only file bytes, never cacheable. Unknown,
    /// foreign, and cross-conversation ids are indistinguishable privacy
    /// 404s; no signed URLs exist in V1.
    /// </summary>
    [HttpGet("conversations/{conversationId:guid}/attachments/{attachmentId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentFile(
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetAttachmentFileQuery(
                userId,
                new ConversationId(conversationId),
                new AttachmentId(attachmentId)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ToChatActionResult(result);
        }

        Response.GetTypedHeaders().CacheControl =
            new Microsoft.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        Response.Headers.Pragma = "no-cache";

        // FileStreamResult disposes the stream after sending.
        return File(result.Value.Content, result.Value.ContentType);
    }

    private IActionResult ToChatActionResult(Result result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : ToActionResult(result);

    private IActionResult ToChatActionResult<T>(Result<T> result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : ToActionResult(result);

    /// <summary>
    /// Both the conversation code (endpoints 1/2/10, and upload scope
    /// failures) and the attachment code (endpoint 6 serve denials) are
    /// privacy failures. Upload validation codes deliberately do NOT match:
    /// they surface as 400s to the uploader.
    /// </summary>
    private static bool IsPrivacyFailure(Result result) =>
        result.IsFailure &&
        (result.Error.Code == ConversationErrors.NotFound.Code ||
         result.Error.Code == AttachmentErrors.NotFound.Code);

    /// <summary>
    /// Chat forces the status/detail pair for its privacy failures here
    /// instead of editing the shared ResultProblemDetailsMapper (which
    /// parallel lanes also touch). The detail is the generic 404 text: the
    /// caller learns nothing about whether the conversation or attachment
    /// exists, who owns it, or why it was denied.
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
