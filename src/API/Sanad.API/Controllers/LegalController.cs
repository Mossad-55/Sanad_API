using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-12 app legal content. The audience is derived from the JWT
/// <c>account_type</c> claims, which carry one entry per owned account. The
/// optional <c>audience</c> query parameter names the audience the caller
/// wants when the token owns more than one (for example <c>?audience=5</c>
/// for a Care Home Owner); account switching is client-local and does not
/// reissue a JWT. Reads return only the current Published version for
/// (document type, audience).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/legal")]
public sealed class LegalController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public LegalController(
        ISender sender)
    {
        _sender = sender;
    }

    /// <param name="audience">
    /// Optional <c>LegalAudience</c> value the caller already owns:
    /// <c>1</c> Family, <c>2</c> MedicalCaregiver, <c>3</c> CompanionCaregiver,
    /// <c>4</c> Elderly, <c>5</c> CareHomeOwner (the enum name is also
    /// accepted, case-insensitive). Care Home Owner is <c>5</c> here although
    /// <c>AccountType.CareHomeOwner</c> is <c>8</c> — the two value spaces are
    /// never cast into one another. Malformed or undefined values are
    /// <c>400 Cms.Content.InvalidAudience</c>; an omitted value while the
    /// token owns more than one audience is
    /// <c>400 Cms.Content.AudienceRequired</c>; a well-formed but unowned value
    /// is <c>403 Cms.Content.UnsupportedAudience</c>.
    /// </param>
    [HttpGet("privacy-policy")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrivacyPolicy(
        CancellationToken cancellationToken,
        [FromQuery] string? audience = null)
    {
        return await GetPublishedAsync(
            LegalDocumentType.PrivacyPolicy,
            audience,
            cancellationToken);
    }

    /// <param name="audience">
    /// Optional <c>LegalAudience</c> value the caller already owns (see the
    /// privacy-policy route): <c>1</c> Family, <c>2</c>
    /// MedicalCaregiver, <c>3</c> CompanionCaregiver, <c>4</c> Elderly,
    /// <c>5</c> CareHomeOwner. Same validation and status codes as the
    /// privacy-policy route.
    /// </param>
    [HttpGet("terms")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTerms(
        CancellationToken cancellationToken,
        [FromQuery] string? audience = null)
    {
        return await GetPublishedAsync(
            LegalDocumentType.TermsAndConditions,
            audience,
            cancellationToken);
    }

    private async Task<IActionResult> GetPublishedAsync(
        LegalDocumentType documentType,
        string? requestedAudience,
        CancellationToken cancellationToken)
    {
        if (!TryResolveContentAudience(
                requestedAudience,
                out LegalAudience audience,
                out IActionResult? failure))
        {
            return failure!;
        }

        var result =
            await _sender.Send(
                new GetPublishedLegalDocumentQuery(
                    documentType,
                    audience),
                cancellationToken);

        return ToActionResult(result);
    }
}
