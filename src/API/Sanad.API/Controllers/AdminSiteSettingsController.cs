using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Settings;

namespace Sanad.API.Controllers;

/// <summary>
/// The admin authoring surface of the one site-settings row: the bilingual
/// navbar links and the bilingual SEO defaults. It uses the existing CMS
/// content policy; no new policy and no new endpoint family is introduced.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/site-settings")]
public sealed class AdminSiteSettingsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(SiteSettingsAdminResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken) =>
        ToSiteSettingsResult(await sender.Send(
            new GetAdminSiteSettingsQuery(),
            cancellationToken));

    [HttpPut]
    [ProducesResponseType(
        typeof(SiteSettingsAdminResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSiteSettingsRequest request,
        CancellationToken cancellationToken)
    {
        // A null element is not an authored link. The write is refused here,
        // before the row is read or written, so the stored document stays
        // unchanged and the answer is the documented 400 of this surface.
        if (request.NavbarLinks?.Any(link => link is null) == true)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                SiteSettingsErrors.InvalidCode);
        }

        return ToSiteSettingsResult(await sender.Send(
            new UpdateSiteSettingsCommand(
                request.NavbarLinks?
                    // The guard above refuses a null element; the projection
                    // stays null-safe so that no request shape can end in an
                    // unhandled failure.
                    .Select(link => link is null
                        ? new SiteSettingsNavbarLinkInput()
                        : new SiteSettingsNavbarLinkInput(
                            link.Key,
                            link.ArabicLabel,
                            link.EnglishLabel,
                            link.Url))
                    .ToList(),
                request.Seo is null
                    ? null
                    : new SiteSettingsSeoInput(
                        request.Seo.ArabicTitle,
                        request.Seo.EnglishTitle,
                        request.Seo.ArabicDescription,
                        request.Seo.EnglishDescription)),
            cancellationToken));
    }

    /// <summary>
    /// Local mapping for this phase's bounded Cms.SiteSettings.* codes only;
    /// the shared ResultProblemDetailsMapper stays untouched.
    /// </summary>
    private IActionResult ToSiteSettingsResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        int statusCode =
            result.Error.Code == SiteSettingsErrors.NotFoundCode
                ? StatusCodes.Status404NotFound
                : result.Error.Code == SiteSettingsErrors.ConflictCode
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;

        return ProblemResult(statusCode, result.Error.Code);
    }

    private IActionResult ProblemResult(int statusCode, string code)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = statusCode switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Bad Request"
            },
            Status = statusCode,
            Detail = "The request could not be completed.",
            Instance = HttpContext.Request.Path
        };

        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return StatusCode(statusCode, problemDetails);
    }
}

/// <summary>
/// The whole settings document that one authoring request replaces. An omitted
/// navbar link list means "no navbar links"; an omitted SEO block means "no
/// SEO defaults". Nothing is generated or kept implicitly.
/// </summary>
public sealed record UpdateSiteSettingsRequest(
    IReadOnlyList<SiteSettingsNavbarLinkRequest?>? NavbarLinks = null,
    SiteSettingsSeoRequest? Seo = null);

public sealed record SiteSettingsNavbarLinkRequest(
    string? Key = null,
    string? ArabicLabel = null,
    string? EnglishLabel = null,
    string? Url = null);

public sealed record SiteSettingsSeoRequest(
    string? ArabicTitle = null,
    string? EnglishTitle = null,
    string? ArabicDescription = null,
    string? EnglishDescription = null);
