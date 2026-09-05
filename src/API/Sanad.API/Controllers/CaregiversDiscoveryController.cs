using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

[AllowAnonymous]
[Route("api/v1/caregivers")]
public sealed class CaregiversDiscoveryController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiversDiscoveryController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CaregiverSearchCardResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchCaregivers(
        [FromQuery] string? search,
        [FromQuery] CaregiverType? type,
        [FromQuery] Gender? gender,
        [FromQuery] Guid? governorateId,
        [FromQuery] Guid? cityId,
        [FromQuery] Guid? areaId,
        [FromQuery] Guid? specializationId,
        [FromQuery] CaregiverAvailability? availability,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] decimal? minRating,
        [FromQuery] int? minExperienceYears,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new SearchCaregiversQuery(
            search,
            type,
            gender,
            governorateId,
            cityId,
            areaId,
            specializationId,
            availability,
            minPrice,
            maxPrice,
            minRating,
            minExperienceYears,
            page,
            pageSize);

        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}")]
    [ProducesResponseType(typeof(CaregiverPublicProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCaregiverProfile(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetCaregiverPublicProfileQuery(new CaregiverId(caregiverId)),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{caregiverId:guid}/quote")]
    [ProducesResponseType(
        typeof(BookingQuoteResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CalculateQuote(
        Guid caregiverId,
        [FromQuery] BookingShiftType shiftType,
        [FromQuery] TimeOnly startTime,
        [FromQuery] TimeOnly endTime,
        CancellationToken cancellationToken)
    {
        var query = new CalculateBookingQuoteQuery(
            new CaregiverId(caregiverId),
            shiftType,
            startTime,
            endTime);

        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }
}