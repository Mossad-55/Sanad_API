using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-13 signed-in Help Center read surface. The audience is derived from the
/// JWT <c>account_type</c> claims (all five app audiences, including Care Home
/// Owner). The optional <c>audience</c> query parameter names the audience the
/// caller wants when the token owns more than one. The read is non-destructive
/// and never fails because the CMS has not been seeded: an empty FAQ list and a
/// null support contact are valid 200 responses.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/help-center")]
public sealed class HelpCenterController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public HelpCenterController(
        ISender sender)
    {
        _sender = sender;
    }

    /// <param name="audience">
    /// Optional Help Center audience the caller already owns:
    /// <c>1</c> Family, <c>2</c> MedicalCaregiver, <c>3</c> CompanionCaregiver,
    /// <c>4</c> Elderly, <c>5</c> CareHomeOwner (the enum name is also
    /// accepted, case-insensitive). Malformed or undefined values are
    /// <c>400 Cms.Content.InvalidAudience</c>; an omitted value while the token
    /// owns more than one audience is
    /// <c>400 Cms.Content.AudienceRequired</c>; a well-formed but unowned value,
    /// or a caller with no app audience, is
    /// <c>403 Cms.Content.UnsupportedAudience</c>.
    /// </param>
    [HttpGet]
    [ProducesResponseType(
        typeof(HelpCenterResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHelpCenter(
        CancellationToken cancellationToken,
        [FromQuery] string? audience = null)
    {
        if (!TryResolveContentAudience(
                audience,
                out LegalAudience resolvedAudience,
                out IActionResult? failure))
        {
            return failure!;
        }

        var result =
            await _sender.Send(
                new GetHelpCenterQuery(
                    resolvedAudience),
                cancellationToken);

        return ToActionResult(result);
    }
}
