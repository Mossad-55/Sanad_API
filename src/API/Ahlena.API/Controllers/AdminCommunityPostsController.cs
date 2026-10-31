using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Posts;

namespace Ahlena.API.Controllers;

/// <summary>
/// FUR-07 Community moderation. Keeps the existing Community moderation
/// policy (normal authenticated SuperAdmin or ContentAdmin) for every
/// moderation read/action. The route family stays under
/// <c>api/v1/admin/community</c>; the report-resolution, retention-read and
/// author-restriction routes use absolute paths so they can live in this one
/// owned controller.
/// <para>
/// Statuses for the new Community.* codes are forced here rather than in the
/// shared ResultProblemDetailsMapper (whose manifest is outside this phase):
/// unknown report/restriction answers 404 and a double resolve or a second
/// active restriction answers 409. Validation codes fall through to the
/// mapper's 400 default.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CommunityModeration)]
[Route("api/v1/admin/community/posts")]
public sealed class AdminCommunityPostsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminCommunityPostsPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] string? reportStatus = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetAdminCommunityPostsQuery(status, reportStatus, search, page, pageSize),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    [HttpGet("{postId:guid}")]
    [ProducesResponseType(typeof(AdminCommunityPostDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAdminCommunityPostDetailQuery(postId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    /// <summary>
    /// Retained restriction reads (F4). Optional author and active filters with
    /// the existing moderation paging conventions; the role policy is the
    /// controller-level Community moderation policy, so no route-level policy
    /// or new action is added.
    /// </summary>
    [HttpGet("/api/v1/admin/community/restrictions")]
    [ProducesResponseType(typeof(AdminCommunityRestrictionsPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListRestrictions(
        [FromQuery] Guid? authorId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetAdminCommunityRestrictionsQuery(authorId, isActive, page, pageSize),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    [HttpPost("{postId:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(new PublishCommunityPostCommand(postId, moderatorId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    [HttpPost("{postId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reject(
        Guid postId,
        [FromBody] RejectCommunityPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(
            new RejectCommunityPostCommand(postId, moderatorId, request.ReasonCode, request.Note),
            cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    /// <summary>
    /// FUR post management (Owner addition, locked 2026-10-23): Community
    /// moderation deletes ANY post, with the deleting moderator recorded in
    /// the post's deletion audit. This is deliberately the only post write
    /// added on this controller — the CommunityModeration permission grants
    /// deletion, not editing or re-attribution: no Admin edit or impersonation
    /// route exists and the app author-ownership rules are unchanged.
    /// <para>
    /// Statuses reuse the shared mapper: unknown or already-deleted post →
    /// 404; a stale concurrent transition against the post's lifecycle
    /// token → 409. Success is 204.
    /// </para>
    /// </summary>
    [HttpDelete("{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid postId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(
            new AdminDeletePostCommand(postId, moderatorId),
            cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    [HttpPost("/api/v1/admin/community/reports/{reportId:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveReport(
        Guid reportId,
        [FromBody] ResolveCommunityPostReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(
            new ResolveCommunityPostReportCommand(reportId, moderatorId, request.Resolution, request.Note),
            cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    [HttpPost("/api/v1/admin/community/authors/{authorId:guid}/restrictions")]
    [ProducesResponseType(typeof(CommunityAuthorRestrictionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRestriction(
        Guid authorId,
        [FromBody] CreateCommunityAuthorRestrictionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(
            new CreateCommunityAuthorRestrictionCommand(
                authorId, moderatorId, request.Duration, request.ReasonCode, request.Note),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    [HttpPost("/api/v1/admin/community/restrictions/{restrictionId:guid}/revoke")]
    [ProducesResponseType(typeof(CommunityAuthorRestrictionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokeRestriction(
        Guid restrictionId,
        [FromBody] RevokeCommunityAuthorRestrictionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId moderatorId)) return Unauthorized();
        var result = await sender.Send(
            new RevokeCommunityAuthorRestrictionCommand(restrictionId, moderatorId, request.Reason),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    private IActionResult ToError(Error error)
    {
        if (error.Code == CommunityModerationErrors.ReportNotFound.Code ||
            error.Code == CommunityModerationErrors.RestrictionNotFound.Code)
        {
            return ForcedProblem(error, StatusCodes.Status404NotFound, "Not Found",
                "The requested resource was not found.");
        }

        if (error.Code == CommunityModerationErrors.ReportAlreadyResolved.Code ||
            error.Code == CommunityModerationErrors.RestrictionAlreadyActive.Code ||
            error.Code == CommunityModerationErrors.RestrictionAlreadyRevoked.Code)
        {
            return ForcedProblem(error, StatusCodes.Status409Conflict, "Conflict",
                "The request conflicts with the current state.");
        }

        return ToActionResult(Result.Failure(error));
    }

    private IActionResult ForcedProblem(Error error, int statusCode, string title, string detail)
    {
        ProblemDetails problemDetails = ResultProblemDetailsMapper.Create(error, HttpContext);
        problemDetails.Type = $"https://httpstatuses.com/{statusCode}";
        problemDetails.Title = title;
        problemDetails.Status = statusCode;
        problemDetails.Detail = detail;
        return StatusCode(statusCode, problemDetails);
    }
}

public sealed record RejectCommunityPostRequest(string? ReasonCode, string? Note);

public sealed record ResolveCommunityPostReportRequest(string? Resolution, string? Note);

public sealed record CreateCommunityAuthorRestrictionRequest(
    string? Duration,
    string? ReasonCode,
    string? Note);

public sealed record RevokeCommunityAuthorRestrictionRequest(string? Reason);
