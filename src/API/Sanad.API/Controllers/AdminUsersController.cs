using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Users.Admin;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] AdminUserListRequest request, CancellationToken ct) =>
        SendQuery(new AdminUserListQuery(request.Search, request.AccountType, request.Status, request.Sort, request.Page, request.PageSize), ct);

    [HttpGet("{userId:guid}")]
    public Task<IActionResult> Detail(Guid userId, CancellationToken ct) =>
        SendQuery(new AdminUserDetailQuery(new UserId(userId)), ct);

    [HttpGet("sessions")]
    public Task<IActionResult> Sessions([FromQuery] AdminUserSessionsRequest request, CancellationToken ct) =>
        SendQuery(new AdminSessionOverviewQuery(request.AccountType, request.UserId, request.Sort, request.Page, request.PageSize), ct);

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
        AdminQueryParsing.TryEnum<Sanad.Modules.Identity.Domain.Users.AccountType>(query.AccountType, out _) &&
        AdminQueryParsing.TryEnum<Sanad.Modules.Identity.Domain.Users.UserStatus>(query.Status, out _);

    private static bool IsValid(AdminSessionOverviewQuery query) =>
        AdminQueryParsing.TryPage(query.Page, query.PageSize, out _, out _) &&
        AdminQueryParsing.TrySort(query.Sort, out _) &&
        AdminQueryParsing.TryEnum<Sanad.Modules.Identity.Domain.Users.AccountType>(query.AccountType, out _) &&
        (string.IsNullOrWhiteSpace(query.UserId) || Guid.TryParse(query.UserId, out var id) && id != Guid.Empty);

    private IActionResult NotFoundProblem(string code) =>
        StatusCode(StatusCodes.Status404NotFound, new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The user was not found.", Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier } });
}
