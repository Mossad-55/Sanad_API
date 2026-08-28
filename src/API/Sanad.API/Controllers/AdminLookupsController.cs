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

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
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