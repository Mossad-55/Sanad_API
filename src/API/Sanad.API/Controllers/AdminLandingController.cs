using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/landing")]
public sealed class AdminLandingController(ISender sender, IFileStorage fileStorage) : ApiControllerBase
{
    [HttpGet("sections")]
    [ProducesResponseType(typeof(LandingAdminPageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSections(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        LandingSectionStatus? sectionStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out LandingSectionStatus parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                return BadRequestWithCode("Cms.Landing.InvalidStatus");
            }

            sectionStatus = parsedStatus;
        }

        LandingSectionType? sectionType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!LandingTypeParser.TryParseKey(type, out LandingSectionType parsedType) &&
                !(Enum.TryParse(type, ignoreCase: true, out parsedType) && Enum.IsDefined(parsedType)))
            {
                return BadRequestWithCode("Cms.Landing.InvalidType");
            }

            sectionType = parsedType;
        }

        return ToActionResult(await sender.Send(
            new GetLandingAdminListQuery(sectionStatus, sectionType, search, page, pageSize),
            cancellationToken));
    }

    [HttpPost("sections")]
    [ProducesResponseType(typeof(LandingAdminSectionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSection(
        [FromBody] CreateLandingSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!LandingTypeParser.TryParseKey(request.Type, out LandingSectionType sectionType))
            return BadRequestWithCode("Cms.Landing.InvalidType");

        var result = await sender.Send(new CreateLandingSectionCommand(
            sectionType,
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicBody,
            request.EnglishBody,
            request.ContentJson,
            request.ImagePath,
            request.DisplayOrder,
            request.IsVisible), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToActionResult(result);
    }

    [HttpPut("sections/{id:guid}")]
    [ProducesResponseType(typeof(LandingAdminSectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSection(
        Guid id,
        [FromBody] UpdateLandingSectionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new UpdateLandingSectionCommand(
            id,
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicBody,
            request.EnglishBody,
            request.ContentJson,
            request.ImagePath,
            request.DisplayOrder,
            request.IsVisible), cancellationToken));

    [HttpPost("sections/{id:guid}/publish")]
    [ProducesResponseType(typeof(LandingAdminSectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishSection(Guid id, CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new PublishLandingSectionCommand(id), cancellationToken));

    [HttpPost("sections/{id:guid}/visibility")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetVisibility(
        Guid id,
        [FromBody] SetLandingSectionVisibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new SetLandingSectionVisibilityCommand(id, request.Value), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpDelete("sections/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSection(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new DeleteLandingSectionCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("sections/reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReorderSections(
        [FromBody] ReorderLandingSectionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ReorderLandingSectionsCommand(
            (request.Items ?? [])
                .Select(item => new LandingReorderItem(item.Id, item.DisplayOrder))
                .ToList()), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("media")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(LandingMediaUploadResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadMedia(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        var upload = await SaveImageAsync(file, cancellationToken);
        if (upload.IsFailure) return ToActionResult(upload);

        string key = upload.Value.Key;
        try
        {
            return StatusCode(
                StatusCodes.Status201Created,
                new LandingMediaUploadResponse(key));
        }
        catch
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
            throw;
        }
    }

    private async Task<Result<StoredFile>> SaveImageAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
            return Result<StoredFile>.Failure(StorageErrors.Empty);
        if (file.Length > 5_242_880)
            return Result<StoredFile>.Failure(StorageErrors.TooLarge);

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        if (!HasMatchingImageSignature(file.ContentType, stream.GetBuffer().AsSpan(0, (int)stream.Length)))
            return Result<StoredFile>.Failure(StorageErrors.UnsupportedType);

        stream.Position = 0;
        return await fileStorage.SaveAsync(stream, file.ContentType, file.Length, "landing", cancellationToken);
    }

    private static bool HasMatchingImageSignature(string contentType, ReadOnlySpan<byte> bytes) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
}

public sealed record CreateLandingSectionRequest(
    string Type,
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder = 0,
    bool IsVisible = true);

public sealed record UpdateLandingSectionRequest(
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder = 0,
    bool IsVisible = true);

public sealed record SetLandingSectionVisibilityRequest(bool Value);

public sealed record LandingReorderEntryRequest(Guid Id, int DisplayOrder);

public sealed record ReorderLandingSectionsRequest(List<LandingReorderEntryRequest>? Items);

public sealed record LandingMediaUploadResponse(string Key);
