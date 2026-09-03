using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Families.Application.Notes;

namespace Sanad.API.Controllers;

[Route("api/v1")]
public sealed class LookupsController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public LookupsController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpGet("lookups/services")]
    [ProducesResponseType(
        typeof(IReadOnlyList<ServicePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveServices(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetActiveServicesQuery(),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpGet("lookups/languages")]
    [ProducesResponseType(
        typeof(IReadOnlyList<LanguagePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveLanguages(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetActiveLanguagesQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [AllowAnonymous]
    [HttpGet("lookups/governorates")]
    [ProducesResponseType(
        typeof(IReadOnlyList<GovernoratePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveGovernorates(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetActiveGovernoratesQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [AllowAnonymous]
    [HttpGet("lookups/cities")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CityPublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveCities(
        [FromQuery] Guid governorateId,
        CancellationToken cancellationToken) =>
            ToActionResult(await _sender.Send(
                new GetActiveCitiesQuery(new GovernorateId(governorateId)), cancellationToken));

    [AllowAnonymous]
    [HttpGet("lookups/areas")]
    [ProducesResponseType(
        typeof(IReadOnlyList<AreaPublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveAreas(
        [FromQuery] Guid cityId,
        CancellationToken cancellationToken) =>
            ToActionResult(await _sender.Send(
                new GetActiveAreasQuery(new CityId(cityId)), cancellationToken));

    [AllowAnonymous]
    [HttpGet("lookups/specializations")]
    [ProducesResponseType(
        typeof(IReadOnlyList<SpecializationPublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSpecializations(
        CancellationToken cancellationToken) =>
            ToActionResult(await _sender.Send(
                new GetActiveSpecializationsQuery(),
                cancellationToken));

    [AllowAnonymous]
    [HttpGet("lookups/professional-titles")]
    [ProducesResponseType(
        typeof(IReadOnlyList<ProfessionalTitlePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveProfessionalTitles(
        CancellationToken cancellationToken) =>
            ToActionResult(await _sender.Send(
                new GetActiveProfessionalTitlesQuery(),
                cancellationToken));

    [AllowAnonymous]
    [HttpGet("lookups/academic-degrees")]
    [ProducesResponseType(
        typeof(IReadOnlyList<AcademicDegreePublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveAcademicDegrees(
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new GetActiveAcademicDegreesQuery(),
            cancellationToken));

    [HttpGet("note-categories")]
    [ProducesResponseType(
        typeof(NoteLookupsResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoteCategories(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetNoteLookupsQuery(),
            cancellationToken);

        return ToActionResult(result);
    }
}