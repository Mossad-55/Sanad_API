using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.Modules.Cms.Application.Legal;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.API.Controllers;

/// <summary>
/// T9-B anonymous public legal content. Anyone — no token at all — can read the
/// current Published <b>Privacy Policy</b> and <b>Terms &amp; Conditions</b>
/// authored for the <see cref="LegalAudience.Public"/> audience. This is the
/// minimal public set decided by the Owner (decision 6A); the Help Center
/// stays authenticated.
/// <para>
/// The audience is fixed to <see cref="LegalAudience.Public"/> by this
/// controller and is never derived from a token, so the authenticated app
/// reads (<c>/api/v1/legal/*</c>) are unchanged and Public is never served
/// there. The response is the same <see cref="LegalDocumentResponse"/> shape
/// (ordered bilingual sections included) the app read returns.
/// </para>
/// </summary>
[AllowAnonymous]
[Route("api/v1/public/legal")]
public sealed class PublicLegalController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public PublicLegalController(
        ISender sender)
    {
        _sender = sender;
    }

    /// <param name="documentType">
    /// The legal document kind as a <see cref="LegalDocumentType"/> value:
    /// <c>1</c> PrivacyPolicy, <c>2</c> TermsAndConditions (the enum name is
    /// also accepted, case-insensitive). No other value exists, so a malformed
    /// or undefined type is <c>400 Cms.Content.InvalidDocumentType</c>. When no
    /// Published Public version exists for the requested type, the read is the
    /// documented <c>404 Cms.Legal.NotPublished</c> — there is never a fallback
    /// to another audience or to static text, and never a draft.
    /// </param>
    [HttpGet("{documentType}")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string documentType,
        CancellationToken cancellationToken)
    {
        if (!TryParseDocumentType(
                documentType,
                out LegalDocumentType parsed))
        {
            return BadRequestWithCode(
                ContentErrors.InvalidDocumentTypeCode);
        }

        var result =
            await _sender.Send(
                new GetPublishedLegalDocumentQuery(
                    parsed,
                    LegalAudience.Public),
                cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Parses exactly one document type: a numeric enum value (1 or 2) or an
    /// enum name, case-insensitive. A comma-joined list, an unknown name or an
    /// undefined number is not a single type and is refused.
    /// </summary>
    private static bool TryParseDocumentType(
        string? value,
        out LegalDocumentType documentType)
    {
        documentType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed =
            value.Trim();

        if (trimmed.IndexOf(',') >= 0)
        {
            return false;
        }

        if (Enum.TryParse(
                trimmed,
                ignoreCase: true,
                out LegalDocumentType parsed) &&
            Enum.IsDefined(parsed))
        {
            documentType = parsed;

            return true;
        }

        return false;
    }
}
