using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.API.Controllers;

/// <summary>
/// E04 SuperAdmin-only platform control read/update. Exposes the operational/
/// maintenance state, the approved bilingual public message, the Card/Wallet
/// switches and the optimistic concurrency version. Gateway secrets and
/// integration ids are never read or written here.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.PlatformControlsAdmin)]
[Route("api/v1/admin/platform-controls")]
public sealed class AdminPlatformControlsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PlatformControlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => ToActionResult(await sender.Send(new GetPlatformControlQuery(), ct));

    [HttpPut]
    [ProducesResponseType(typeof(PlatformControlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(UpdatePlatformControlRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new UpdatePlatformControlCommand(
                request.ExpectedVersion,
                request.State,
                request.MaintenanceMessageArabic,
                request.MaintenanceMessageEnglish,
                request.CardEnabled,
                request.WalletEnabled,
                actorUserId.Value,
                request.InternalReason),
            ct);

        return ToActionResult(result);
    }
}

/// <summary>
/// Full-replacement update body. Every control value is supplied explicitly;
/// omitted switches default to disabled (fail closed) rather than staying
/// silently enabled. <see cref="InternalReason"/> is stored only in the
/// append-only audit record and is never returned publicly.
/// </summary>
public sealed record UpdatePlatformControlRequest(
    int ExpectedVersion,
    PlatformState State,
    bool CardEnabled,
    bool WalletEnabled,
    string? MaintenanceMessageArabic = null,
    string? MaintenanceMessageEnglish = null,
    string? InternalReason = null);
