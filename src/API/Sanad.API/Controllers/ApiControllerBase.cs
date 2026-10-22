using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Cms.Application.Legal;
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
    /// Coded 400 for a malformed or undefined audience value.
    /// </summary>
    protected const string InvalidContentAudienceCode =
        "Cms.Content.InvalidAudience";

    /// <summary>
    /// Coded 400 for an omitted audience while the token owns more than one.
    /// </summary>
    protected const string ContentAudienceRequiredCode =
        "Cms.Content.AudienceRequired";

    /// <summary>
    /// Every legal / Help Center audience the Normal JWT actually owns. A
    /// Normal token carries one <c>account_type</c> claim per owned account in
    /// <c>user.Accounts</c> order: duplicates are collapsed and the result is
    /// ordered by enum value, so the resolved audience never depends on claim
    /// order (SET-12/SET-13). Admin account types — SuperAdmin, ContentAdmin,
    /// SupportAdmin and FinanceAdmin — are not app audiences and never map, so
    /// an admin calling an app content route is denied instead of being served
    /// an invented document. <see cref="LegalAudience"/> is a distinct value
    /// space from <see cref="AccountType"/>: CareHomeOwner maps from 8 to 5 by
    /// name, never by casting.
    /// </summary>
    protected IReadOnlyList<LegalAudience> GetOwnedContentAudiences()
    {
        return User
            .FindAll(
                AuthClaimNames.AccountType)
            .Select(
                claim =>
                    MapAccountTypeToAudience(
                        claim.Value))
            .Where(
                audience =>
                    audience.IsDefined())
            .Distinct()
            .OrderBy(
                audience =>
                    audience)
            .ToArray();
    }

    /// <summary>
    /// Resolves the legal / Help Center audience for a Normal app read.
    /// <paramref name="requestedAudience"/> is optional and must name an
    /// audience the caller owns:
    /// omitted with exactly one owned audience selects it; omitted with more
    /// than one is <c>400 Cms.Content.AudienceRequired</c>; omitted with none
    /// keeps the documented <c>403 Cms.Content.UnsupportedAudience</c> denial;
    /// a malformed or undefined value is <c>400 Cms.Content.InvalidAudience</c>;
    /// an owned value is served, and a valid value the caller does not own is
    /// <c>403 Cms.Content.UnsupportedAudience</c>. Returns false with
    /// <paramref name="failure"/> when the caller cannot be served.
    /// </summary>
    protected bool TryResolveContentAudience(
        string? requestedAudience,
        out LegalAudience audience,
        out IActionResult? failure)
    {
        audience = default;
        failure = null;

        if (!string.IsNullOrWhiteSpace(
                requestedAudience))
        {
            if (!TryParseContentAudience(
                    requestedAudience,
                    out LegalAudience requested))
            {
                failure = BadRequestWithCode(
                    InvalidContentAudienceCode);

                return false;
            }

            if (!GetOwnedContentAudiences()
                    .Contains(requested))
            {
                failure = ToActionResult(
                    Result.Failure(
                        ContentErrors.UnsupportedAudience));

                return false;
            }

            audience = requested;

            return true;
        }

        IReadOnlyList<LegalAudience> owned =
            GetOwnedContentAudiences();

        if (owned.Count == 1)
        {
            audience = owned[0];

            return true;
        }

        failure = owned.Count == 0
            ? ToActionResult(
                Result.Failure(
                    ContentErrors.UnsupportedAudience))
            : BadRequestWithCode(
                ContentAudienceRequiredCode);

        return false;
    }

    /// <summary>
    /// Parses exactly one audience value: a numeric enum value (1–5) or an
    /// enum name, case-insensitive. A comma-joined list, an unknown name or an
    /// undefined number is not a single audience and is refused.
    /// </summary>
    protected static bool TryParseContentAudience(
        string? value,
        out LegalAudience audience)
    {
        audience = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed =
            value.Trim();

        if (trimmed.IndexOf(',') >= 0)
        {
            return false;
        }

        if (Enum.TryParse(
                trimmed,
                ignoreCase: true,
                out LegalAudience parsed) &&
            parsed.IsDefined())
        {
            audience = parsed;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Single-audience audience probe kept for the existing claims-mapping
    /// test. Legal and Help Center routes resolve through
    /// <see cref="TryResolveContentAudience"/>, which is claim-order
    /// independent; this helper returns the first owned audience in enum
    /// order (or false when the token owns no app audience).
    /// </summary>
    protected bool TryGetCmsAudienceFromClaims(
        out LegalAudience audience)
    {
        audience = default;

        IReadOnlyList<LegalAudience> owned =
            GetOwnedContentAudiences();

        if (owned.Count == 0)
        {
            return false;
        }

        audience = owned[0];

        return true;
    }

    private static LegalAudience MapAccountTypeToAudience(
        string? accountType)
    {
        return accountType switch
        {
            nameof(AccountType.Family) =>
                LegalAudience.Family,
            nameof(AccountType.MedicalCaregiver) =>
                LegalAudience.MedicalCaregiver,
            nameof(AccountType.CompanionCaregiver) =>
                LegalAudience.CompanionCaregiver,
            nameof(AccountType.Elderly) =>
                LegalAudience.Elderly,
            nameof(AccountType.CareHomeOwner) =>
                LegalAudience.CareHomeOwner,
            _ => default
        };
    }

    protected bool TryGetCurrentDeviceSessionId(
        out DeviceSessionId deviceSessionId)
    {
        deviceSessionId = DeviceSessionId.Empty;

        // Verified session binding comes from the JWT sid claim, never from
        // a client-selected header. The header remains only for the explicit
        // logout-current flow that names the device session to revoke.
        string? claim =
            User.FindFirst(
                AuthClaimNames.SessionId)?.Value;

        if (Guid.TryParse(
                claim,
                out Guid claimValue) &&
            claimValue != Guid.Empty)
        {
            deviceSessionId = new DeviceSessionId(claimValue);
            return true;
        }

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

    protected bool TryGetVerifiedSessionId(
        out DeviceSessionId deviceSessionId)
    {
        deviceSessionId = DeviceSessionId.Empty;

        string? claim =
            User.FindFirst(
                AuthClaimNames.SessionId)?.Value;

        return Guid.TryParse(
            claim,
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