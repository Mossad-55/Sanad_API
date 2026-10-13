using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.Blocks;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Presence;
using Sanad.Modules.Chat.Application.Privacy;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Application.Reports;
using Sanad.Modules.Chat.Application.Sync;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.API.Controllers;

/// <summary>
/// Chat app routes (endpoints 1–4, 5–11 of the frozen contract, plus the
/// Chat Plus sync/privacy/presence additions). All routes resolve the
/// participant from the JWT; unknown or foreign conversations,
/// messages, attachments, files, block targets, and reports answer with a
/// privacy-preserving 404 that never reveals why. The support side of the
/// report flow (endpoints 12–15) lives in
/// <see cref="AdminChatReportsController"/>.
/// Normal access only: restricted-verification tokens are rejected for Chat
/// (401/403 by the policy before any handler runs).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/chat")]
public sealed class ChatController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IChatRateGate _rateGate;

    public ChatController(ISender sender, IChatRateGate? rateGate = null)
    {
        _sender = sender;
        _rateGate = rateGate ?? new InMemoryChatRateGate();
    }

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

    /// <summary>
    /// Endpoint 2 — open-or-get (200, never a duplicate). Exactly one
    /// target is required (Opus delta): <c>recipientUserId</c> opens a
    /// direct chat; <c>bookingId</c> opens a booking chat whose counterpart
    /// the backend resolves from the booking (the booking must exist, have
    /// the caller as a party, and not be cancelled/expired/refunded). Both
    /// or neither answers 400
    /// <see cref="ConversationErrors.OpenTargetInvalid"/>; every
    /// resource-shaped denial stays the generic privacy 404.
    /// </summary>
    [HttpPost("conversations")]
    [ProducesResponseType(
        typeof(OpenConversationResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OpenConversation(
        [FromBody] OpenConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        UserId? recipient = request.RecipientUserId is { } value
            ? new UserId(value)
            : null;

        BookingId? bookingId = request.BookingId is { } booking
            ? new BookingId(booking)
            : null;

        var result = await _sender.Send(
            new OpenConversationCommand(
                userId,
                recipient,
                bookingId),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 3 — cursor page of a conversation's messages, newest first
    /// by (<c>createdOnUtc</c>, <c>id</c>). <c>pageSize</c> defaults to 30
    /// and is clamped to 1–100 by the handler; <c>beforeId</c> accepts the
    /// opaque <c>nextCursor</c> this endpoint returned or a raw message id.
    /// Unknown, foreign, and hidden-for-requester conversations, and any
    /// <c>beforeId</c> that does not resolve inside this
    /// conversation, are the generic privacy 404 — the parameter can never
    /// be used to probe ids. The response also carries the delivery write
    /// side effect: the recipient's first fetch stamps
    /// <c>deliveredOnUtc</c>.
    /// </summary>
    [HttpGet("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType(
        typeof(MessageCursorPage),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        [FromQuery] string? beforeId = null,
        [FromQuery] int pageSize = GetMessagesQueryHandler.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetMessagesQuery(
                userId,
                new ConversationId(conversationId),
                beforeId,
                pageSize),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 4 — send a message (201 on create, 200 on an exact replay of
    /// the same <c>clientMessageId</c> with the same content; the body is
    /// the message item either way, and the status code alone tells the two
    /// apart). A reused key with different content is the frozen 409
    /// <see cref="MessageErrors.IdempotencyConflict"/>, forced here rather
    /// than in the shared ResultProblemDetailsMapper (which has no Chat.*
    /// entries and would default an unmapped code to 400).
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType(
        typeof(MessageItemResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(MessageItemResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendMessage(
        Guid conversationId,
        [FromBody] ChatSendMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        AttachmentId? attachmentId = request.AttachmentId is { } value
            ? new AttachmentId(value)
            : null;

        var result = await _sender.Send(
            new SendMessageCommand(
                userId,
                new ConversationId(conversationId),
                request.ClientMessageId,
                request.Type,
                request.Text,
                attachmentId,
                request.Location?.Latitude,
                request.Location?.Longitude,
                request.Location?.Label),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ToChatActionResult(result);
        }

        return result.Value.Created
            ? StatusCode(StatusCodes.Status201Created, result.Value.Message)
            : Ok(result.Value.Message);
    }

    /// <summary>
    /// Endpoint 8 — the requester's unread totals: messages they did not
    /// send that the read rule does not count as read, plus how many
    /// conversations hold at least one such message. Conversations the
    /// requester hid are excluded; a requester with nothing visible answers
    /// 0/0.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(
        typeof(UnreadCountResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetUnreadCountQuery(userId),
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
    /// Endpoint 7 — record the participant's read mark (204, idempotent).
    /// POST as of the Opus delta (was PUT; semantics unchanged). One row
    /// per (conversation, user): repeating the call updates the
    /// same row and answers 204 again. The conversation/participant gate
    /// runs before anything else, so unknown and foreign conversation ids
    /// are the same generic privacy 404 and never reveal existence; only a
    /// participant who sent an empty/missing lastReadMessageId gets the 400
    /// <see cref="Conversations.ConversationReadErrors.Invalid"/>. The
    /// message id itself is recorded verbatim — validating it against
    /// messages is Lane B's job (Lane A has no Message table), and a read
    /// mark never changes last activity, hiding, or visibility.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkConversationRead(
        Guid conversationId,
        [FromBody] ChatMarkReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new MarkConversationReadCommand(
                userId,
                new ConversationId(conversationId),
                request.LastReadMessageId),
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
    /// Abuse control: at most 2 concurrent uploads per user (rejected before
    /// buffering) plus 10/min; both answer 429 with Retry-After.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52_428_800)]
    [ProducesResponseType(
        typeof(AttachmentUploadResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadAttachment(
        Guid conversationId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        if (!_rateGate.TryEnterUpload(userId, out TimeSpan slotRetry))
        {
            return RateLimited(ChatRateErrors.Uploads(slotRetry));
        }

        try
        {
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
        finally
        {
            _rateGate.ExitUpload(userId);
        }
    }

    /// <summary>
    /// Endpoint 6 — participant-only file bytes, never cacheable. The route
    /// carries the attachment id only (Opus delta): the participant check
    /// runs against the conversation the attachment belongs to, so a client
    /// can never steer it. Unknown and foreign attachment ids are
    /// indistinguishable privacy 404s; no signed URLs exist in V1.
    /// </summary>
    [HttpGet("attachments/{attachmentId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentFile(
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

    /// <summary>
    /// Endpoint 9 — block a user (204, idempotent). Self-block is a 400
    /// <see cref="BlockErrors.Invalid"/>: the caller is entitled to know
    /// their own request was invalid. An unknown target user answers the
    /// generic privacy 404, so this route never confirms whether an arbitrary
    /// user id is registered. The blocked sender's send-time failure is
    /// deliberately identical to a not-participant failure (Lane B).
    /// </summary>
    [HttpPost("blocks")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockUser(
        [FromBody] BlockUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new BlockUserCommand(userId, new UserId(request.BlockedUserId)),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 9 — unblock (204, idempotent). A missing row still succeeds:
    /// unblock is the blocker's own action and never reveals whether a block
    /// existed. Removing the row restores sending (Lane B send-time check).
    /// </summary>
    [HttpDelete("blocks/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnblockUser(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new UnblockUserCommand(actor, new UserId(userId)),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 9 — the caller's blocked user ids, most-recent block first.
    /// Per-user: never lists ids blocked by anyone else.
    /// </summary>
    [HttpGet("blocks")]
    [ProducesResponseType(typeof(Guid[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedUsers(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor))
        {
            return Unauthorized();
        }

        Result<Guid[]> result = await _sender.Send(
            new GetBlockedUsersQuery(actor),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Endpoint 11 — submit a report over a conversation (or one message in
    /// it) → 201 <c>{reportId}</c>. Only a participant may report, so an
    /// unknown or foreign conversation and an unknown or cross-conversation
    /// <c>messageId</c> are the same generic privacy 404
    /// (<see cref="ReportErrors.NotFound"/>); an out-of-bounds reason is the
    /// caller's own 400 (<see cref="ReportErrors.Invalid"/>). Review,
    /// dismiss, and uphold are Support's side of the flow (endpoints 12–15).
    /// </summary>
    [HttpPost("reports")]
    [ProducesResponseType(
        typeof(SubmitReportResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitReport(
        [FromBody] ChatSubmitReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        MessageId? messageId = request.MessageId is { } value
            ? new MessageId(value)
            : null;

        var result = await _sender.Send(
            new SubmitReportCommand(
                userId,
                new ConversationId(request.ConversationId),
                messageId,
                request.Reason),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ToChatActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    /// <summary>
    /// Chat Plus sync — commit-safe REST recovery for sockets, degraded
    /// polling, app foreground, and restart. Same participant/visibility
    /// authorization as history; page size clamps 1–100 (default 100).
    /// An empty cursor starts the authoritative snapshot; subsequent reads
    /// consume committed changes. Malformed/foreign cursors are the privacy
    /// 404; an unsupported cursor version returns an empty page with
    /// `resetRequired: true`.
    /// </summary>
    [HttpGet("conversations/{conversationId:guid}/sync")]
    [ProducesResponseType(
        typeof(ConversationSyncPage),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SyncConversation(
        Guid conversationId,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = GetConversationSyncQueryHandler.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetConversationSyncQuery(
                userId,
                new ConversationId(conversationId),
                cursor,
                pageSize),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Chat Plus presence privacy — the caller's own settings. Absence of a
    /// row means undisclosed (both false for new and legacy users).
    /// </summary>
    [HttpGet("privacy")]
    [ProducesResponseType(
        typeof(ChatPrivacyResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrivacy(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetChatPrivacyQuery(userId),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Chat Plus presence privacy — full replacement; both booleans are
    /// required and never defaulted from omitted fields. A change evicts
    /// affected presence subscriptions before further dispatch (the realtime
    /// layer re-resolves eligibility per dispatch).
    /// </summary>
    [HttpPut("privacy")]
    [ProducesResponseType(
        typeof(ChatPrivacyResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePrivacy(
        [FromBody] ChatUpdatePrivacyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new UpdateChatPrivacyCommand(
                userId,
                request.ShowOnlineStatus,
                request.ShareLastSeen),
            cancellationToken);

        return ToChatActionResult(result);
    }

    /// <summary>
    /// Chat Plus authorized presence snapshot for a conversation's
    /// counterpart. `isOnline` null means undisclosed/unknown (not offline);
    /// either-direction block suppresses both fields without removing REST
    /// history access. Clients apply the snapshot after privacy changes,
    /// authorization changes, foreground, and reconnect.
    /// </summary>
    [HttpGet("conversations/{conversationId:guid}/presence")]
    [ProducesResponseType(
        typeof(ConversationPresenceResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPresence(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetConversationPresenceQuery(
                userId,
                new ConversationId(conversationId)),
            cancellationToken);

        return ToChatActionResult(result);
    }

    private IActionResult ToChatActionResult(Result result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : IsRateLimited(result)
                ? RateLimited(result.Error)
                : IsConflict(result)
                    ? SendConflict(result.Error)
                    : ToActionResult(result);

    private IActionResult ToChatActionResult<T>(Result<T> result) =>
        IsPrivacyFailure(result)
            ? PrivacyNotFound(result.Error)
            : IsRateLimited(result)
                ? RateLimited(result.Error)
                : IsConflict(result)
                    ? SendConflict(result.Error)
                    : ToActionResult(result);

    /// <summary>
    /// The conversation code (endpoints 1/2/7/10, and upload scope
    /// failures), the attachment code (endpoint 6 serve denials), the block
    /// code (endpoint 9's unknown target user), the message code
    /// (endpoint 3's unresolvable <c>beforeMessageId</c>), the sync code
    /// (Chat Plus endpoint's unknown/foreign/hidden conversation and
    /// malformed/foreign/requester-mismatched cursor), and the report
    /// code (endpoint 11's unknown/foreign conversation and unknown/
    /// cross-conversation messageId) are privacy failures: one
    /// indistinguishable generic 404 each. Upload validation codes,
    /// Chat.Message.Invalid, Chat.Block.Invalid, Chat.Report.Invalid,
    /// Chat.ConversationRead.Invalid, and Chat.Conversation.OpenTargetInvalid
    /// deliberately do NOT match: they surface
    /// as 400s to the caller, who is entitled to know their own request was
    /// invalid. Chat.Report.NotFound is forced here only on the app route —
    /// the admin controller renders the same code as a plain 404, where
    /// privacy forcing has no meaning (Support is never a participant).
    /// </summary>
    private static bool IsPrivacyFailure(Result result) =>
        result.IsFailure &&
        (result.Error.Code == ConversationErrors.NotFound.Code ||
          result.Error.Code == AttachmentErrors.NotFound.Code ||
          result.Error.Code == BlockErrors.NotFound.Code ||
          result.Error.Code == MessageErrors.NotFound.Code ||
          result.Error.Code == ChatSyncErrors.NotFound.Code ||
          result.Error.Code == ReportErrors.NotFound.Code);

    /// <summary>
    /// Chat Plus 429: abuse-control failures answer Too Many Requests with
    /// the earliest retry delay and never reveal account/resource details.
    /// </summary>
    private static bool IsRateLimited(Result result) =>
        result.IsFailure &&
        result.Error.Code.StartsWith(
            "Chat.RateLimited.",
            StringComparison.Ordinal);

    private IActionResult RateLimited(Error error)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = "https://httpstatuses.com/429";
        problemDetails.Title = "Too Many Requests";
        problemDetails.Status = StatusCodes.Status429TooManyRequests;
        problemDetails.Detail = "The rate limit was reached. Retry after the indicated delay.";

        if (error.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math
                .Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            problemDetails);
    }

    /// <summary>
    /// Endpoint 4's idempotency 409. Like the privacy failures, Chat forces
    /// the status/detail pair here instead of adding Chat.* entries to the
    /// shared ResultProblemDetailsMapper: any unmapped code there defaults
    /// to a 400, and the frozen contract pins this one at 409.
    /// </summary>
    private static bool IsConflict(Result result) =>
        result.IsFailure &&
        result.Error.Code == MessageErrors.IdempotencyConflict.Code;

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

    /// <summary>
    /// The 409 counterpart of <see cref="PrivacyNotFound"/>: the code, title,
    /// status, and generic detail are forced here for
    /// Chat.Message.IdempotencyConflict. The body's own error code/message
    /// (returned by the mapper) stay intact so a client can key on them.
    /// </summary>
    private IActionResult SendConflict(Error error)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = "https://httpstatuses.com/409";
        problemDetails.Title = "Conflict";
        problemDetails.Status = StatusCodes.Status409Conflict;
        problemDetails.Detail = "The request conflicts with the current state.";

        return StatusCode(
            StatusCodes.Status409Conflict,
            problemDetails);
    }
}
