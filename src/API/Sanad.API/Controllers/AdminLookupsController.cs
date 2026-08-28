using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiversAdmin)]
[Route("api/v1/admin")]
public sealed class AdminLookupsController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public AdminLookupsController(
        ISender sender,
        IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
    }

    [HttpGet("lookups/services")]
    [ProducesResponseType(
        typeof(IReadOnlyList<ServiceResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllServices(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetAllServicesQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("lookups/languages")]
    [ProducesResponseType(
        typeof(IReadOnlyList<LanguageResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllLanguages(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetAllLanguagesQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("lookups/governorates")]
    [ProducesResponseType(
        typeof(IReadOnlyList<GovernorateResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllGovernorates(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetAllGovernoratesQuery(),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/services")]
    [RequestSizeLimit(2_097_152)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(ServiceResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateService(
        [FromForm] CreateServiceRequest request,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var upload =
            await SaveIconAsync(
                file,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string iconKey =
            upload.Value.Key;

        var result =
            await _sender.Send(
                new CreateServiceCommand(
                    request.ArabicName,
                    request.EnglishName,
                    upload.Value.Key,
                    request.CaregiverType,
                    request.IsActive),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                iconKey,
                cancellationToken);

            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPut("lookups/services/{id:guid}")]
    public async Task<IActionResult> RenameService(
        Guid id,
        [FromBody] RenameServiceRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new RenameServiceCommand(
                new ServiceId(id),
                request.ArabicName,
                request.EnglishName);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("lookups/services/{id:guid}/activate")]
    public async Task<IActionResult> ActivateService(
        Guid id,
        CancellationToken cancellationToken)
    {
        var command =
            new SetServiceActiveCommand(
                new ServiceId(id),
                true);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("lookups/services/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateService(
        Guid id,
        CancellationToken cancellationToken)
    {
        var command =
            new SetServiceActiveCommand(
                new ServiceId(id),
                false);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("lookups/languages")]
    [Consumes("application/json")]
    [ProducesResponseType(
    typeof(LanguageResponse),
    StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateLanguage(
    [FromBody] CreateLanguageRequest request,
    CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateLanguageCommand(
                    request.Code,
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPut("lookups/languages/{id:guid}")]
    public async Task<IActionResult> RenameLanguage(
        Guid id,
        [FromBody] RenameLanguageRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RenameLanguageCommand(
                    new LanguageId(id),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/languages/{id:guid}/activate")]
    public async Task<IActionResult> ActivateLanguage(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SetLanguageActiveCommand(
                    new LanguageId(id),
                    true),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/languages/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateLanguage(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SetLanguageActiveCommand(
                    new LanguageId(id),
                    false),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/governorates")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(GovernorateResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGovernorate(
        [FromBody] CreateGovernorateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateGovernorateCommand(
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPut("lookups/governorates/{id:guid}")]
    public async Task<IActionResult> RenameGovernorate(
        Guid id,
        [FromBody] RenameGovernorateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RenameGovernorateCommand(
                    new GovernorateId(id),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/governorates/{id:guid}/activate")]
    public async Task<IActionResult> ActivateGovernorate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SetGovernorateActiveCommand(
                    new GovernorateId(id),
                    true),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/governorates/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateGovernorate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new SetGovernorateActiveCommand(
                    new GovernorateId(id),
                    false),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/cities")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(CityResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCity(
        [FromBody] CreateCityRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateCityCommand(
                    new GovernorateId(request.GovernorateId),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("lookups/cities/{id:guid}")]
    public async Task<IActionResult> RenameCity(
        Guid id,
        [FromBody] RenameCityRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RenameCityCommand(
                    new CityId(id),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/cities/{id:guid}/activate")]
    public async Task<IActionResult> ActivateCity(
        Guid id,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new SetCityActiveCommand(new CityId(id), true), cancellationToken));

    [HttpPost("lookups/cities/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateCity(
        Guid id,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new SetCityActiveCommand(new CityId(id), false), cancellationToken));

    [HttpGet("lookups/cities")]
    [ProducesResponseType(
        typeof(IReadOnlyList<CityResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllCities(
        [FromQuery] Guid governorateId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new GetAllCitiesQuery(new GovernorateId(governorateId)), cancellationToken));

    [HttpPost("lookups/areas")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(AreaResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateArea(
        [FromBody] CreateAreaRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateAreaCommand(
                    new CityId(request.CityId),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("lookups/areas/{id:guid}")]
    public async Task<IActionResult> RenameArea(
        Guid id,
        [FromBody] RenameAreaRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RenameAreaCommand(
                    new AreaId(id),
                    request.ArabicName,
                    request.EnglishName),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("lookups/areas/{id:guid}/activate")]
    public async Task<IActionResult> ActivateArea(
        Guid id,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new SetAreaActiveCommand(new AreaId(id), true), cancellationToken));

    [HttpPost("lookups/areas/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateArea(
        Guid id,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new SetAreaActiveCommand(new AreaId(id), false), cancellationToken));

    [HttpGet("lookups/areas")]
    [ProducesResponseType(
        typeof(IReadOnlyList<AreaResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAreas(
        [FromQuery] Guid cityId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _sender.Send(
            new GetAllAreasQuery(new CityId(cityId)), cancellationToken));

    private async Task<Result<StoredFile>> SaveIconAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        await using Stream stream =
            file.OpenReadStream();

        return await _fileStorage.SaveAsync(
            stream,
            file.ContentType,
            file.Length,
            folder: "services",
            cancellationToken);
    }
}