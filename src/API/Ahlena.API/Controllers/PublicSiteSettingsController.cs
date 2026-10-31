using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.Modules.Cms.Application.Settings;

namespace Ahlena.API.Controllers;

/// <summary>
/// The anonymous public read of the published site settings. It exposes only
/// the navbar links and the SEO defaults; before anything is authored it
/// returns an empty link list and no SEO block, never invented content.
/// </summary>
[Route("api/v1/site-settings")]
public sealed class PublicSiteSettingsController(ISender sender) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(
        typeof(PublicSiteSettingsResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken) =>
        ToActionResult(await sender.Send(
            new GetPublicSiteSettingsQuery(),
            cancellationToken));
}
