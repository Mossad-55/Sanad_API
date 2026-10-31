using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.Cms.Application.MedicationLateness;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/cms/medication-lateness")]
public sealed class AdminMedicationLatenessController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(MedicationLatenessSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct) => ToActionResult(await sender.Send(new GetMedicationLatenessSettingQuery(), ct));

    [HttpPost("revisions")]
    [ProducesResponseType(typeof(MedicationLatenessSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateMedicationLatenessSettingRevisionRequest request, CancellationToken ct)
        => ToActionResult(await sender.Send(new CreateMedicationLatenessSettingRevisionCommand(request.ThresholdMinutes), ct));
}

public sealed record CreateMedicationLatenessSettingRevisionRequest(int ThresholdMinutes);
