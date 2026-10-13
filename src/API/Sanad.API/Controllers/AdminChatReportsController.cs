using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Reports;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.API.Controllers;

/// <summary>
/// Support endpoints 12–15: the report queue, the read-only reported-
/// conversation view, and the dismiss/uphold decisions. This is the only
/// route family allowed to read a conversation the caller does not
/// participate in, so it sits behind the <c>ChatSupportAdmin</c> policy
/// (SuperAdmin + SupportAdmin) instead of the participant resolution every
/// app route uses; there is deliberately no participant check in the
/// handlers.
/// <para>
/// Statuses are forced here rather than in the shared
/// ResultProblemDetailsMapper (which has no Chat.* entries and defaults
/// unmapped codes to 400): an unknown report answers a plain 404
/// (<see cref="ReportErrors.NotFound"/>) and a double resolve answers 409
/// (<see cref="ReportErrors.InvalidState"/>) with its code intact.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ChatSupportAdmin)]
[Route("api/v1/admin/chat/reports")]
public sealed class AdminChatReportsController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Endpoint 12 — report queue, newest first, optionally filtered by
    /// <c>status</c> (Open/Dismissed/Upheld) and paged. An unrecognised
    /// status value is a 400 <see cref="ReportErrors.Invalid"/>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await Send(new GetReportsQuery(status, page, pageSize), cancellationToken);

    /// <summary>
    /// Endpoint 13 — one report's detail (fields, status, resolution audit).
    /// Unknown report → 404.
    /// </summary>
    [HttpGet("{reportId:guid}")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(
        Guid reportId,
        CancellationToken cancellationToken) =>
        await Send(new GetReportQuery(new ReportId(reportId)), cancellationToken);

    /// <summary>
    /// Endpoint 13 — the reported conversation's messages, newest first,
    /// endpoint 3 item shape. The only non-participant read path; read-only
    /// (no delivery/read stamping). Unknown report → 404.
    /// Bounded compatibility (deprecated): conversations with more than
    /// 1,000 messages answer 409
    /// <see cref="ReportErrors.HistoryRequiresPagination"/> — use
    /// <see cref="MessagesPage"/> instead. Never a truncated array.
    /// </summary>
    [HttpGet("{reportId:guid}/messages")]
    [ProducesResponseType(typeof(MessageItemResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [Obsolete("Use GET reports/{id}/messages/page for bounded history reads.")]
    public async Task<IActionResult> Messages(
        Guid reportId,
        CancellationToken cancellationToken) =>
        await Send(new GetReportMessagesQuery(new ReportId(reportId)), cancellationToken);

    /// <summary>
    /// Endpoint 13 (paged) — the reported conversation's messages in the
    /// existing <c>MessageCursorPage</c> shape, newest first with
    /// <c>beforeId</c> pagination (opaque cursor or raw message id of this
    /// conversation). Read-only like the array endpoint. Unknown report or
    /// unresolvable cursor → 404.
    /// </summary>
    [HttpGet("{reportId:guid}/messages/page")]
    [ProducesResponseType(typeof(MessageCursorPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MessagesPage(
        Guid reportId,
        [FromQuery] string? beforeId = null,
        [FromQuery] int pageSize = GetReportMessagesPageQueryHandler.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        await Send(
            new GetReportMessagesPageQuery(
                new ReportId(reportId),
                beforeId,
                pageSize),
            cancellationToken);

    /// <summary>
    /// Endpoint 14 — close the report with history retained (204). Unknown
    /// report → 404; already resolved → 409.
    /// </summary>
    [HttpPost("{reportId:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Dismiss(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        if (!TryActor(out UserId actor))
        {
            return Unauthorized();
        }

        return await Send(
            new DismissReportCommand(actor, new ReportId(reportId)),
            cancellationToken);
    }

    /// <summary>
    /// Endpoint 15 — record the violation and apply the mutual chat-block
    /// between the conversation's two participants (204). Unknown report →
    /// 404; already resolved → 409 (and no block row is touched).
    /// </summary>
    [HttpPost("{reportId:guid}/uphold")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Uphold(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        if (!TryActor(out UserId actor))
        {
            return Unauthorized();
        }

        return await Send(
            new UpholdReportCommand(actor, new ReportId(reportId)),
            cancellationToken);
    }

    private async Task<IActionResult> Send<T>(
        IQuery<T> query,
        CancellationToken cancellationToken) =>
        ToResponse(await sender.Send(query, cancellationToken));

    private async Task<IActionResult> Send(
        ICommand command,
        CancellationToken cancellationToken) =>
        ToResponse(await sender.Send(command, cancellationToken));

    private IActionResult ToResponse<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : ToError(result.Error);

    private IActionResult ToResponse(Result result) =>
        result.IsSuccess ? NoContent() : ToError(result.Error);

    /// <summary>
    /// The forced statuses (404 unknown report, 409 double resolve or
    /// oversized legacy history); everything else falls through to the
    /// shared mapper, where the unmapped Chat.Report.Invalid correctly lands
    /// on 400. The code and traceId extensions survive the rewrite, so a
    /// client can still key on <c>Chat.Report.*</c>.
    /// </summary>
    private IActionResult ToError(Error error)
    {
        if (error.Code == ReportErrors.NotFound.Code)
        {
            return ForcedProblem(
                error,
                StatusCodes.Status404NotFound,
                "Not Found",
                "The requested resource was not found.");
        }

        if (error.Code == ReportErrors.InvalidState.Code)
        {
            return ForcedProblem(
                error,
                StatusCodes.Status409Conflict,
                "Conflict",
                "The request conflicts with the current state.");
        }

        if (error.Code == ReportErrors.HistoryRequiresPagination.Code)
        {
            return ForcedProblem(
                error,
                StatusCodes.Status409Conflict,
                "Conflict",
                "The reported conversation is too large for the legacy view; use the paged messages endpoint.");
        }

        return ToActionResult(Result.Failure(error));
    }

    private IActionResult ForcedProblem(
        Error error,
        int statusCode,
        string title,
        string detail)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = $"https://httpstatuses.com/{statusCode}";
        problemDetails.Title = title;
        problemDetails.Status = statusCode;
        problemDetails.Detail = detail;

        return StatusCode(statusCode, problemDetails);
    }

    /// <summary>
    /// Resolution audit actor, taken from the JWT <c>sub</c> claim exactly
    /// like the other admin controllers. The two helpers the elderly-ops
    /// controllers also carry (Account type, correlation id) are omitted
    /// here: the Chat application commands take the actor alone — no Chat
    /// command or query consumes an account type or a correlation id, so
    /// those helpers would be dead code. A missing or malformed <c>sub</c>
    /// answers 401 instead of throwing (review hardening: never a 500 from
    /// claim parsing).
    /// </summary>
    private bool TryActor(out UserId actor)
    {
        actor = UserId.Empty;

        string? sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(sub, out Guid actorId) || actorId == Guid.Empty)
        {
            return false;
        }

        actor = new UserId(actorId);
        return true;
    }
}
