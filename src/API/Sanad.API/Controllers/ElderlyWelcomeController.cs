using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.Modules.Cms.Application.Welcome;

namespace Sanad.API.Controllers;

[AllowAnonymous]
[Route("api/v1/elderly/welcome")]
public sealed class ElderlyWelcomeController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string language = "en", CancellationToken ct = default)
    {
        if (!language.Equals("ar", StringComparison.OrdinalIgnoreCase) && !language.Equals("en", StringComparison.OrdinalIgnoreCase)) return BadRequest();
        return ToActionResult(await sender.Send(new GetPublishedElderlyWelcomeQuery(language), ct));
    }
}
