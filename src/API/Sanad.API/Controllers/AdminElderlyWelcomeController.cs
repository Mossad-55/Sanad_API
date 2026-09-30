using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.Welcome;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/cms/elderly-welcome")]
public sealed class AdminElderlyWelcomeController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => ToActionResult(await sender.Send(new GetElderlyWelcomeQuery(), ct));
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ElderlyWelcomeRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateElderlyWelcomeCommand(request.ArabicHeadline, request.EnglishHeadline, request.ArabicCtaLabel, request.EnglishCtaLabel, request.Benefits), ct);
        return result.IsFailure ? ToActionResult(result) : StatusCode(StatusCodes.Status201Created, result.Value);
    }
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ElderlyWelcomeRequest request, CancellationToken ct) => ToActionResult(await sender.Send(new UpdateElderlyWelcomeCommand(request.ArabicHeadline, request.EnglishHeadline, request.ArabicCtaLabel, request.EnglishCtaLabel, request.Benefits), ct));
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(CancellationToken ct) => ToActionResult(await sender.Send(new PublishElderlyWelcomeCommand(), ct));
    [HttpPost("unpublish")]
    public async Task<IActionResult> Unpublish(CancellationToken ct) => ToActionResult(await sender.Send(new UnpublishElderlyWelcomeCommand(), ct));
}

public sealed record ElderlyWelcomeRequest(string ArabicHeadline, string EnglishHeadline, string ArabicCtaLabel, string EnglishCtaLabel, IReadOnlyList<ElderlyWelcomeBenefitInput> Benefits);
