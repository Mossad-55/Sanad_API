using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.MedicationLateness;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/cms/medication-lateness")]
public sealed class AdminMedicationLatenessController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => ToActionResult(await sender.Send(new GetMedicationLatenessSettingQuery(), ct));

    [HttpPost("revisions")]
    public async Task<IActionResult> Create([FromBody] CreateMedicationLatenessSettingRevisionRequest request, CancellationToken ct)
        => ToActionResult(await sender.Send(new CreateMedicationLatenessSettingRevisionCommand(request.ThresholdMinutes), ct));
}

public sealed record CreateMedicationLatenessSettingRevisionRequest(int ThresholdMinutes);
