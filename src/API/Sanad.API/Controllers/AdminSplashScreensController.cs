using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Splash;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/splash-screens")]
public sealed class AdminSplashScreensController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public AdminSplashScreensController(
        ISender sender,
        IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
    }

    [HttpPost]
    [RequestSizeLimit(2_097_152)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SplashScreenResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
    [FromForm] CreateSplashScreenRequest request,
    IFormFile file,
    CancellationToken cancellationToken)
    {
        var upload =
            await SaveSplashImageAsync(
                file,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(upload);
        }

        string imageKey =
            upload.Value.Key;

        var result =
            await _sender.Send(
                new CreateSplashScreenCommand(
                    request.InternalName,
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicDescription,
                    request.EnglishDescription,
                    request.ArabicButtonText,
                    request.EnglishButtonText,
                    upload.Value.Key,
                    request.BackgroundColor,
                    request.DisplayOrder),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                imageKey,
                cancellationToken);

            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPut("{id:guid}")]
    [RequestSizeLimit(2_097_152)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] UpdateSplashScreenRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        string? imagePath = null;

        if (file is not null)
        {
            var upload =
                await SaveSplashImageAsync(
                    file,
                    cancellationToken);

            if (upload.IsFailure)
            {
                return ToActionResult(upload);
            }

            imagePath = upload.Value.Key;
        }

        var result =
            await _sender.Send(
                new UpdateSplashScreenCommand(
                    new SplashScreenId(id),
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicDescription,
                    request.EnglishDescription,
                    request.ArabicButtonText,
                    request.EnglishButtonText,
                    imagePath,
                    request.BackgroundColor,
                    request.DisplayOrder),
                cancellationToken);

        if (result.IsFailure &&
            imagePath is not null)
        {
            await _fileStorage.DeleteAsync(
                imagePath,
                cancellationToken);
        }

        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new PublishSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Unpublish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UnpublishSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new DeleteSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    private async Task<Result<StoredFile>> SaveSplashImageAsync(
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
            folder: "splash",
            cancellationToken);
    }
}