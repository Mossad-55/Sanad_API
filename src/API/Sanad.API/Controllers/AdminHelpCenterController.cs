using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-13 admin Help Center. SuperAdmin and ContentAdmin only (CmsContent);
/// SupportAdmin stays read-only. FAQ management is non-destructive (no
/// delete route in this slice) and the support contact is one global row.
/// Nothing here sends SMS, email, or notifications.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/help-center")]
public sealed class AdminHelpCenterController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminHelpCenterController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("faqs")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(HelpFaqResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFaq(
        [FromBody] CreateHelpFaqRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateHelpFaqCommand(
                    request.Audience,
                    request.ArabicQuestion,
                    request.EnglishQuestion,
                    request.ArabicAnswer,
                    request.EnglishAnswer,
                    request.DisplayOrder,
                    request.IsActive),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet("faqs")]
    [ProducesResponseType(
        typeof(IReadOnlyList<HelpFaqResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFaqs(
        [FromQuery] LegalAudience? audience,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ListHelpFaqsQuery(
                    audience,
                    isActive),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("faqs/{id:guid}")]
    [ProducesResponseType(
        typeof(HelpFaqResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFaqById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetHelpFaqByIdQuery(
                    new HelpFaqId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("faqs/{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(HelpFaqResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFaq(
        Guid id,
        [FromBody] UpdateHelpFaqRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UpdateHelpFaqCommand(
                    new HelpFaqId(id),
                    request.Audience,
                    request.ArabicQuestion,
                    request.EnglishQuestion,
                    request.ArabicAnswer,
                    request.EnglishAnswer,
                    request.DisplayOrder,
                    request.IsActive),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("faqs/{id:guid}/activate")]
    [ProducesResponseType(
        typeof(HelpFaqResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateFaq(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ActivateHelpFaqCommand(
                    new HelpFaqId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("faqs/{id:guid}/deactivate")]
    [ProducesResponseType(
        typeof(HelpFaqResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateFaq(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new DeactivateHelpFaqCommand(
                    new HelpFaqId(id)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("support-contact")]
    [ProducesResponseType(
        typeof(SupportContactResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupportContact(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetSupportContactQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("support-contact")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(SupportContactResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertSupportContact(
        [FromBody] UpdateSupportContactRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UpsertSupportContactCommand(
                    request.SupportPhone,
                    request.SupportEmail),
                cancellationToken);

        return ToActionResult(result);
    }
}
