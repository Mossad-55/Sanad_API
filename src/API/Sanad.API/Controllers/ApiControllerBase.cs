using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Cms.Domain.Legal;
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

    /// <summary>
    /// Maps the authenticated JWT AccountType claim to the four CMS app
    /// audiences (SET-12/SET-13). Admin account types are not app
    /// audiences: SuperAdmin, ContentAdmin, and SupportAdmin never resolve
    /// an audience, so an admin calling an app content route receives the
    /// mapped business error Cms.Content.UnsupportedAudience (403) instead
    /// of an invented document. There is deliberately no audience supplied
    /// by the request.
    /// </summary>
    protected bool TryGetCmsAudienceFromClaims(
        out LegalAudience audience)
    {
        audience = default;

        string? accountType =
            User.FindFirst(
                AuthClaimNames.AccountType)?.Value;

        audience = accountType switch
        {
            nameof(AccountType.Family) =>
                LegalAudience.Family,
            nameof(AccountType.MedicalCaregiver) =>
                LegalAudience.MedicalCaregiver,
            nameof(AccountType.CompanionCaregiver) =>
                LegalAudience.CompanionCaregiver,
            nameof(AccountType.Elderly) =>
                LegalAudience.Elderly,
            _ => default
        };

        return audience.IsDefined();
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