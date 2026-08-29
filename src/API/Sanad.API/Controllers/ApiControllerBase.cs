using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.API.Controllers;

[ApiController]
public abstract class ApiControllerBase :
    ControllerBase
{
    protected const string DeviceSessionHeaderName =
        "X-Device-Session-Id";

    protected IActionResult ToActionResult(
        Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(
                result.Error,
                HttpContext);

        return StatusCode(
            problemDetails.Status ??
            StatusCodes.Status500InternalServerError,
            problemDetails);
    }

    protected IActionResult ToActionResult<T>(
        Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(
                result.Error,
                HttpContext);

        return StatusCode(
            problemDetails.Status ??
            StatusCodes.Status500InternalServerError,
            problemDetails);
    }

    protected bool TryGetAuthenticatedUserId(
        out UserId userId)
    {
        userId = UserId.Empty;

        string? subject =
            User.FindFirst(
                JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(
            subject,
            out Guid value) &&
            value != Guid.Empty &&
            (userId = new UserId(value)) !=
                UserId.Empty;
    }

    protected bool TryGetCaregiverTypeFromClaims(
        out CaregiverType caregiverType)
    {
        caregiverType = default;

        string? accountType =
            User.FindFirst(
                AuthClaimNames.AccountType)?.Value;

        caregiverType = accountType switch
        {
            nameof(AccountType.MedicalCaregiver) =>
                CaregiverType.Medical,
            nameof(AccountType.CompanionCaregiver) =>
                CaregiverType.Companion,
            _ => default
        };

        return caregiverType is
            CaregiverType.Medical or
            CaregiverType.Companion;
    }

    protected bool TryGetCurrentDeviceSessionId(
        out DeviceSessionId deviceSessionId)
    {
        deviceSessionId = DeviceSessionId.Empty;

        if (!Request.Headers.TryGetValue(
                DeviceSessionHeaderName,
                out var headerValue))
        {
            return false;
        }

        return Guid.TryParse(
            headerValue.ToString(),
            out Guid value) &&
            value != Guid.Empty &&
            (deviceSessionId = new DeviceSessionId(value)) !=
                DeviceSessionId.Empty;
    }

    protected IActionResult BadRequestWithCode(
        string code)
    {
        var problemDetails =
            new ProblemDetails
            {
                Type =
                    "https://httpstatuses.com/400",
                Title = "Bad Request",
                Status =
                    StatusCodes.Status400BadRequest,
                Detail =
                    "The request could not be completed.",
                Instance =
                    HttpContext.Request.Path
            };

        problemDetails.Extensions["code"] =
            code;

        problemDetails.Extensions["traceId"] =
            HttpContext.TraceIdentifier;

        return BadRequest(problemDetails);
    }
}