using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-12 app legal content. The audience is always derived from the JWT;
/// there is no audience query parameter or body field. Reads return only the
/// current Published version for (document type, audience).
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

    [HttpGet("privacy-policy")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrivacyPolicy(
        CancellationToken cancellationToken)
    {
        return await GetPublishedAsync(
            LegalDocumentType.PrivacyPolicy,
            cancellationToken);
    }

    [HttpGet("terms")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTerms(
        CancellationToken cancellationToken)
    {
        return await GetPublishedAsync(
            LegalDocumentType.TermsAndConditions,
            cancellationToken);
    }

    private async Task<IActionResult> GetPublishedAsync(
        LegalDocumentType documentType,
        CancellationToken cancellationToken)
    {
        if (!TryGetCmsAudienceFromClaims(
                out LegalAudience audience))
        {
            return ToActionResult(
                Result.Failure(
                    ContentErrors.UnsupportedAudience));
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
