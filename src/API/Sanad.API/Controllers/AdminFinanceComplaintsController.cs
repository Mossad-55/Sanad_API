using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.API.Controllers;

public sealed record OpenComplaintReviewRequest(
    Guid RefundCaseId,
    Guid FamilyId,
    ComplaintReportingParty ReportingParty,
    DateTime ReportedOnUtc,
    DateTime? VisitedOnUtc = null,
    bool IsSafetyConcern = false,
    string? Description = null,
    // The user credited with reporting. Defaults to the authenticated
    // administrator opening the case on the reporter's behalf (#14: the
    // reporting party and reporter are mandatory evidence).
    Guid? ReportedByUserId = null);

public sealed record BeginComplaintReviewInvestigationRequest(
    DateTime StartedOnUtc,
    string? EvidenceReference = null);

public sealed record ApproveComplaintReviewRequest(
    DateTime ApprovedOnUtc,
    long AwardMinorUnits = 0,
    string? DecisionNote = null,
    string? EvidenceReference = null);

public sealed record RefuseComplaintReviewRequest(
    DateTime RefusedOnUtc,
    string? DecisionNote = null);

/// <summary>
/// Reviewed-complaint workflow (clause 5, Owner decisions #11-#14) under the
/// shared Finance admin surface.
/// <para>
/// The class-level <see cref="AuthorizationPolicies.RefundWorkflowAdmin"/>
/// policy admits SuperAdmin and SupportAdmin to the case surface: SupportAdmin
/// receives, investigates and reads a case. The verdict is narrower — owner
/// decision #12 makes approve and refuse
/// <see cref="AuthorizationPolicies.SuperAdminOnly"/> at the endpoint, and the
/// application seam enforces the same split from the actor's account type, so
/// the role rule cannot be bypassed by a caller that reaches the handler
/// directly. FinanceAdmin is excluded from every route here.
/// </para>
/// <para>
/// Approval records the verdict and the resulting money movement in one unit of
/// work (#16): the awarded amount is appended to the case's append-only ledger
/// as a pure movement record (#35), so approval is never itself completion and a
/// zero award (#31) is recorded as computed.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RefundWorkflowAdmin)]
[Route("api/v1/admin/finance/complaint-reviews")]
public sealed class AdminFinanceComplaintsController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open(
        OpenComplaintReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new OpenComplaintReviewCommand(
                request.RefundCaseId,
                request.FamilyId,
                request.ReportingParty,
                request.ReportedByUserId ?? actorUserId.Value,
                request.ReportedOnUtc,
                request.VisitedOnUtc,
                request.IsSafetyConcern,
                request.Description),
            cancellationToken);

        if (result.IsSuccess)
            return StatusCode(StatusCodes.Status201Created, result.Value);

        return ComplaintFailure(result.Error);
    }

    [HttpGet("{complaintReviewId:guid}")]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid complaintReviewId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetComplaintReviewQuery(complaintReviewId), cancellationToken);

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ComplaintReviewSnapshot>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] ComplaintReviewState? state = null,
        [FromQuery] Guid? familyId = null,
        CancellationToken cancellationToken = default)
        => Ok((await sender.Send(new ListComplaintReviewsQuery(state, familyId), cancellationToken)).Value);

    [HttpGet("by-refund-case/{refundCaseId:guid}")]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForCase(Guid refundCaseId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetComplaintReviewForCaseQuery(refundCaseId), cancellationToken);

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpPost("{complaintReviewId:guid}/investigate")]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Investigate(
        Guid complaintReviewId,
        BeginComplaintReviewInvestigationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new BeginComplaintReviewInvestigationCommand(
                complaintReviewId,
                actorUserId.Value,
                request.StartedOnUtc,
                request.EvidenceReference),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ComplaintFailure(result.Error);
    }

    [HttpPost("{complaintReviewId:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid complaintReviewId,
        ApproveComplaintReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new ApproveComplaintReviewCommand(
                complaintReviewId,
                actorUserId.Value,
                CurrentAccountType,
                request.ApprovedOnUtc,
                request.AwardMinorUnits,
                request.DecisionNote,
                request.EvidenceReference),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ComplaintFailure(result.Error);
    }

    [HttpPost("{complaintReviewId:guid}/refuse")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(typeof(ComplaintReviewSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refuse(
        Guid complaintReviewId,
        RefuseComplaintReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new RefuseComplaintReviewCommand(
                complaintReviewId,
                actorUserId.Value,
                CurrentAccountType,
                request.RefusedOnUtc,
                request.DecisionNote),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ComplaintFailure(result.Error);
    }

    /// <summary>
    /// The authenticated actor's account type claim, forwarded to the seam so the
    /// #12 SuperAdmin-only verdict rule is enforced inside the handler too, not
    /// only by the endpoint policy. Null when the claim is absent, which the
    /// guard then treats as not-a-SuperAdmin.
    /// </summary>
    private string? CurrentAccountType => User.FindFirst(AuthClaimNames.AccountType)?.Value;

    private IActionResult ComplaintFailure(Error error)
    {
        int status = error.Code switch
        {
            "Finance.Complaint.NotFound" => StatusCodes.Status404NotFound,
            "Finance.Complaint.Conflict" => StatusCodes.Status409Conflict,
            "Finance.Complaint.SuperAdminRequired" => StatusCodes.Status403Forbidden,
            "Finance.Complaint.AmountInvalid" => StatusCodes.Status400BadRequest,
            // The award is a valid amount but violates the configured policy
            // ceiling, so it is a conflict with current policy — not malformed
            // input. This matches the shared mapper and the documented 409.
            "Finance.Complaint.AwardExceedsCap" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status403Forbidden => "Forbidden",
                _ => "Bad Request"
            },
            Detail = error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
