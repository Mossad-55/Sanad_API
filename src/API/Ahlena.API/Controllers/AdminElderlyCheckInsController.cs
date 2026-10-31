using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.CheckIns;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.ElderlyMedicationOperationalRead)]
[Route("api/v1/admin/elderly/check-ins")]
public sealed class AdminElderlyCheckInsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedAdminElderlyCheckIns), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? elderlyId = null, [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, [FromQuery] bool? answer = null, CancellationToken ct = default)
        => await Send(new GetAdminElderlyCheckInsQuery(Actor(), AccountType(), CorrelationId(), page, pageSize, elderlyId, startDate, endDate, answer), ct);

    [HttpGet("{checkInId:guid}")]
    [ProducesResponseType(typeof(AdminElderlyCheckInRecord), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid checkInId, CancellationToken ct) => await Send(new GetAdminElderlyCheckInQuery(Actor(), AccountType(), CorrelationId(), checkInId), ct);

    [HttpGet("timeline/{elderlyId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminElderlyCheckInRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Timeline(Guid elderlyId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken ct) => await Send(new GetAdminElderlyCheckInTimelineQuery(Actor(), AccountType(), CorrelationId(), elderlyId, startDate, endDate), ct);

    [HttpGet("aggregate")]
    [ProducesResponseType(typeof(AdminElderlyCheckInAggregate), StatusCodes.Status200OK)]
    public async Task<IActionResult> Aggregate([FromQuery] Guid? elderlyId = null, [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, CancellationToken ct = default) => await Send(new GetAdminElderlyCheckInAggregateQuery(Actor(), AccountType(), CorrelationId(), elderlyId, startDate, endDate), ct);

    [HttpGet("missed")]
    [ProducesResponseType(typeof(PagedMissedCheckIns), StatusCodes.Status200OK)]
    public async Task<IActionResult> Missed([FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateOnly startDate, [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateOnly endDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? elderlyId = null, CancellationToken ct = default)
    {
        var result = await sender.Send(new GetAdminMissedCheckInsQuery(Actor(), AccountType(), CorrelationId(), startDate, endDate, page, pageSize, elderlyId), ct);
        return ToMissedActionResult(result);
    }

    private async Task<IActionResult> Send<T>(IQuery<T> query, CancellationToken ct) => ToActionResult(await sender.Send(query, ct));

    private IActionResult ToMissedActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var code = result.Error.Code;
        int status = code switch
        {
            "Families.AdminCheckIn.IndeterminateEvaluation" => StatusCodes.Status409Conflict,
            "Families.AdminCheckIn.EvaluationUnavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => Ahlena.API.ProblemDetail.ResultProblemDetailsMapper.Create(result.Error, HttpContext).Status ?? StatusCodes.Status400BadRequest
        };
        var problemDetails = Ahlena.API.ProblemDetail.ResultProblemDetailsMapper.Create(result.Error, HttpContext);
        problemDetails.Status = status;
        return StatusCode(status, problemDetails);
    }
    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    private string AccountType() => User.FindFirst(AuthClaimNames.AccountType)!.Value;
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : HttpContext.TraceIdentifier;
}
