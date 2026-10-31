using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers.Requests;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Application.Users.Admin;
using Ahlena.Modules.Identity.Application.Authentication.Registration;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] AdminUserListRequest request, CancellationToken ct) =>
        SendQuery(new AdminUserListQuery(request.Search, request.AccountType, request.Status, request.Sort, request.Page, request.PageSize), ct);

    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Detail(Guid userId, CancellationToken ct) =>
        SendQuery(new AdminUserDetailQuery(new UserId(userId)), ct);

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(AdminSessionOverviewResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> Sessions([FromQuery] AdminUserSessionsRequest request, CancellationToken ct) =>
        SendQuery(new AdminSessionOverviewQuery(request.AccountType, request.UserId, request.Sort, request.Page, request.PageSize), ct);

    [HttpPost("{userId:guid}/suspend")]
    [ProducesResponseType(typeof(AdminUserStatusResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> Suspend(Guid userId, AdminUserStatusRequest request, CancellationToken ct) =>
        SendStatus(new SuspendAdminUserCommand(CurrentUserId(), new UserId(userId), request.Reason ?? string.Empty), ct);

    [HttpPost("{userId:guid}/reactivate")]
    [ProducesResponseType(typeof(AdminUserStatusResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(Guid userId, CancellationToken ct) =>
        SendStatus(new ReactivateAdminUserCommand(CurrentUserId(), new UserId(userId)), ct);

    [HttpPost("{userId:guid}/block")]
    [ProducesResponseType(typeof(AdminUserStatusResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> Block(Guid userId, AdminUserStatusRequest request, CancellationToken ct) =>
        SendStatus(new BlockAdminUserCommand(CurrentUserId(), new UserId(userId), request.Reason ?? string.Empty), ct);

    [HttpPost("{userId:guid}/sessions/revoke")]
    public async Task<IActionResult> RevokeSessions(Guid userId, AdminUserSessionRevokeRequest? request, CancellationToken ct)
    {
        Result result = await sender.Send(new RevokeAdminUserSessionsCommand(CurrentUserId(), new UserId(userId), request?.SessionId is Guid sessionId ? new DeviceSessionId(sessionId) : null), ct);
        return ToCommandResult(result);
    }

    [HttpPost("{userId:guid}/password-reset")]
    [ProducesResponseType(typeof(ResetAdminUserPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid userId, CancellationToken ct)
    {
        Result<ResetAdminUserPasswordResponse> result = await sender.Send(new ResetAdminUserPasswordCommand(CurrentUserId(), new UserId(userId)), ct);
        return result.IsSuccess ? Ok(result.Value) : ToCommandResult(result);
    }

    [HttpPut("{userId:guid}/account-type")]
    [ProducesResponseType(typeof(AdminUserAccountTypeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeAccountType(Guid userId, AdminUserAccountTypeChangeRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse(request.AccountType, true, out Ahlena.Modules.Identity.Domain.Users.AccountType accountType) ||
            !ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType(accountType))
            return BadRequestWithCode(AdminUserErrors.IllegalAccountTypeTransition.Code);
        Result<AdminUserAccountTypeResponse> result = await sender.Send(new ChangeAdminUserAccountTypeCommand(CurrentUserId(), new UserId(userId), accountType, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : ToCommandResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RegisterUserResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAdminUser(AdminUserCreateRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse(request.AccountType, true, out Ahlena.Modules.Identity.Domain.Users.AccountType accountType) ||
            !ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType(accountType))
            return BadRequestWithCode(AdminUserErrors.IllegalAccountTypeTransition.Code);
        Result<RegisterUserResponse> result = await sender.Send(new CreateAdminUserCommand(CurrentUserId(), request.ArabicFullName, request.EnglishFullName, request.Email, request.PhoneNumber, request.Password, accountType), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ToActionResult(result);
    }

    [HttpPost("{userId:guid}/anonymize")]
    public async Task<IActionResult> Anonymize(Guid userId, AdminUserReasonRequest request, CancellationToken ct)
    {
        Result result = await sender.Send(new AnonymizeAdminUserCommand(CurrentUserId(), new UserId(userId), request.Reason), ct);
        return ToCommandResult(result);
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(AdminUserAuditListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Audit([FromQuery] AdminUserAuditRequest request, CancellationToken ct)
    {
        Result<AdminUserAuditListResponse> result = await sender.Send(new AdminUserAuditQuery(request.ActorUserId, request.TargetUserId, request.Action, request.FromUtc, request.ToUtc, request.Sort, request.Page, request.PageSize), ct);
        if (result.IsFailure && result.Error.Code == AdminUserErrors.InvalidQuery.Code) return BadRequestWithCode(result.Error.Code);
        return ToActionResult(result);
    }

    [HttpGet("statistics")]
    [ProducesResponseType(typeof(AdminUserStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Statistics(CancellationToken ct) =>
        SendQuery(new AdminUserStatisticsQuery(), ct);

    private async Task<IActionResult> SendQuery<T>(IQuery<T> query, CancellationToken ct)
    {
        if (query is AdminUserListQuery list && !IsValid(list) ||
            query is AdminSessionOverviewQuery sessions && !IsValid(sessions))
            return BadRequestWithCode("Identity.Admin.InvalidQuery");
        Result<T> result = await sender.Send(query, ct);
        if (result.IsFailure && result.Error.Code == AdminUserErrors.NotFound.Code)
            return NotFoundProblem(result.Error.Code);
        if (result.IsFailure && result.Error.Code == AdminUserErrors.InvalidQuery.Code)
            return BadRequestWithCode(result.Error.Code);
        return ToActionResult(result);
    }

    private static bool IsValid(AdminUserListQuery query) =>
        AdminQueryParsing.TryPage(query.Page, query.PageSize, out _, out _) &&
        AdminQueryParsing.TrySort(query.Sort, out _) &&
        AdminQueryParsing.TryEnum<Ahlena.Modules.Identity.Domain.Users.AccountType>(query.AccountType, out _) &&
        AdminQueryParsing.TryEnum<Ahlena.Modules.Identity.Domain.Users.UserStatus>(query.Status, out _);

    private static bool IsValid(AdminSessionOverviewQuery query) =>
        AdminQueryParsing.TryPage(query.Page, query.PageSize, out _, out _) &&
        AdminQueryParsing.TrySort(query.Sort, out _) &&
        AdminQueryParsing.TryEnum<Ahlena.Modules.Identity.Domain.Users.AccountType>(query.AccountType, out _) &&
        (string.IsNullOrWhiteSpace(query.UserId) || Guid.TryParse(query.UserId, out var id) && id != Guid.Empty);

    private IActionResult NotFoundProblem(string code) =>
        StatusCode(StatusCodes.Status404NotFound, new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The user was not found.", Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier } });

    private async Task<IActionResult> SendStatus<T>(ICommand<T> command, CancellationToken ct)
    {
        Result<T> result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result.Value) : ToCommandResult(result);
    }

    private IActionResult ToCommandResult(Result result)
    {
        if (result.IsSuccess) return NoContent();
        int status = result.Error.Code switch
        {
            "Identity.User.NotFound" => 404,
            "Identity.Admin.SeededProtected" or "Identity.Admin.SelfAction" => 403,
            "Identity.Admin.AlreadySuspended" or "Identity.Admin.NotSuspended" or "Identity.Admin.AlreadyBlocked" or "Identity.Admin.LastSuperAdmin" => 409,
            "Identity.Admin.IllegalAccountTypeTransition" => 409,
            "Identity.Admin.ActivationNotReady" => 409,
            "Identity.Admin.AlreadyAnonymized" or "Identity.Admin.LastSuperAdmin" => 409,
            _ => 400
        };
        var problemDetails = new ProblemDetails { Status = status, Title = status == 404 ? "Not Found" : status == 403 ? "Forbidden" : status == 409 ? "Conflict" : "Bad Request", Extensions = { ["code"] = result.Error.Code, ["traceId"] = HttpContext.TraceIdentifier } };
        if (result.Error.Code == AdminUserErrors.ActivationNotReady.Code)
            problemDetails.Detail = result.Error.Message;
        return StatusCode(status, problemDetails);
    }

    private UserId CurrentUserId() => TryGetAuthenticatedUserId(out UserId userId) ? userId : UserId.Empty;
}
