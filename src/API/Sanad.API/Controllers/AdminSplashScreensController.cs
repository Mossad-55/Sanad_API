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

    [HttpGet]
    [ProducesResponseType(
    typeof(IReadOnlyList<SplashScreenResponse>),
    StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(
    CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetAllSplashScreensQuery(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetSplashScreenByIdQuery(new SplashScreenId(id)),
            cancellationToken);

        return ToActionResult(result);
    }

    private const long MaximumImageBytes = 2_097_152;

    private async Task<Result<StoredFile>> SaveSplashImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        if (file.Length > MaximumImageBytes)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.TooLarge);
        }

        // The declared MIME type is never trusted on its own: the raster
        // signature is verified against the actual bytes before anything is
        // written to disk.
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        if (!HasMatchingImageSignature(
                file.ContentType, content.GetBuffer().AsSpan(0, (int)content.Length)))
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        content.Position = 0;
        return await _fileStorage.SaveAsync(
            content,
            file.ContentType,
            file.Length,
            folder: "splash",
            cancellationToken);
    }

    /// <summary>
    /// Byte-signature check for the accepted raster types; any other declared
    /// type — including active content such as SVG — has no allowed signature
    /// and is rejected.
    /// </summary>
    private static bool HasMatchingImageSignature(string contentType, ReadOnlySpan<byte> bytes) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
}