using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-12 admin legal documents. SuperAdmin and ContentAdmin only
/// (CmsContent); SupportAdmin stays read-only. Versioning is server side:
/// Draft -> Published -> Archived, with full history retained.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/legal-documents")]
public sealed class AdminLegalDocumentsController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminLegalDocumentsController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLegalDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateLegalDocumentCommand(
                    request.DocumentType,
                    request.Audience,
                    MapSections(request.Sections)),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<LegalDocumentResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(
        [FromQuery] LegalDocumentType? documentType,
        [FromQuery] LegalAudience? audience,
        [FromQuery] LegalDocumentStatus? status,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ListLegalDocumentsQuery(
                    documentType,
                    audience,
                    status),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetLegalDocumentByIdQuery(
                    new LegalDocumentId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateLegalDocumentSectionsRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UpdateLegalDocumentSectionsCommand(
                    new LegalDocumentId(id),
                    MapSections(request.Sections)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(
        typeof(LegalDocumentResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new PublishLegalDocumentCommand(
                    new LegalDocumentId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    private static IReadOnlyList<LegalSectionInput> MapSections(
        IReadOnlyList<LegalSectionRequest>? sections)
    {
        if (sections is null)
        {
            return [];
        }

        return sections
            .Select(section =>
                new LegalSectionInput(
                    section.SectionType,
                    section.DisplayOrder,
                    section.ArabicTitle,
                    section.EnglishTitle,
                    section.ArabicDescription,
                    section.EnglishDescription,
                    section.ArabicBullets,
                    section.EnglishBullets))
            .ToList();
    }
}
