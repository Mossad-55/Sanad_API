using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/library")]
public sealed class AdminLibraryController(ISender sender, IFileStorage fileStorage) : ApiControllerBase
{
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminLibraryCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new GetLibraryAdminCategoriesQuery(), cancellationToken));

    [HttpPost("categories")]
    [ProducesResponseType(typeof(AdminLibraryCategoryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateLibraryCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new CreateLibraryCategoryCommand(request.ArabicName, request.EnglishName, request.IsActive),
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToActionResult(result);
    }

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(typeof(AdminLibraryCategoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCategory(
        Guid id,
        [FromBody] UpdateLibraryCategoryRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new UpdateLibraryCategoryCommand(id, request.ArabicName, request.EnglishName, request.IsActive),
            cancellationToken));

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new DeleteLibraryCategoryCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpGet("content")]
    [ProducesResponseType(typeof(LibraryAdminPageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContent(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        LibraryContentStatus? contentStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out LibraryContentStatus parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                return BadRequestWithCode("Cms.Library.InvalidStatus");
            }

            contentStatus = parsedStatus;
        }

        LibraryContentType? contentType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse(type, ignoreCase: true, out LibraryContentType parsedType) ||
                !Enum.IsDefined(parsedType))
            {
                return BadRequestWithCode("Cms.Library.InvalidType");
            }

            contentType = parsedType;
        }

        return ToActionResult(await sender.Send(
            new GetLibraryAdminListQuery(contentStatus, contentType, search, page, pageSize),
            cancellationToken));
    }

    [HttpGet("content/{id:guid}")]
    [ProducesResponseType(typeof(LibraryAdminContentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContentDetail(Guid id, CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new GetLibraryAdminContentQuery(id), cancellationToken));

    [HttpPost("content")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(LibraryAdminContentResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateContent(
        [FromForm] CreateLibraryContentRequest request,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseContentRequest(request, out var categoryId, out var contenttype, out var sections, out var parseError))
        {
            return BadRequestWithCode(parseError);
        }

        var upload = await SaveCoverAsync(file, cancellationToken);
        if (upload.IsFailure) return ToActionResult(upload);

        var result = await sender.Send(new CreateLibraryContentCommand(
            contenttype!.Value,
            categoryId,
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicSummary,
            request.EnglishSummary,
            request.VideoUrl,
            request.DurationSeconds,
            upload.Value.Key,
            request.IsPremium,
            sections), cancellationToken);
        if (result.IsFailure)
        {
            await fileStorage.DeleteAsync(upload.Value.Key, cancellationToken);
            return ToActionResult(result);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("content/{id:guid}")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(LibraryAdminContentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateContent(
        Guid id,
        [FromForm] UpdateLibraryContentRequest request,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseContentRequest(request, out var categoryId, out _, out var sections, out var parseError))
        {
            return BadRequestWithCode(parseError);
        }

        string? coverKey = null;
        if (file is not null)
        {
            var upload = await SaveCoverAsync(file, cancellationToken);
            if (upload.IsFailure) return ToActionResult(upload);
            coverKey = upload.Value.Key;
        }

        var result = await sender.Send(new UpdateLibraryContentCommand(
            id,
            categoryId,
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicSummary,
            request.EnglishSummary,
            request.VideoUrl,
            request.DurationSeconds,
            coverKey,
            request.IsPremium,
            sections), cancellationToken);
        if (result.IsFailure && coverKey is not null)
        {
            await fileStorage.DeleteAsync(coverKey, cancellationToken);
        }

        return ToActionResult(result);
    }

    [HttpPost("content/{id:guid}/publish")]
    [ProducesResponseType(typeof(LibraryAdminContentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishContent(Guid id, CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new PublishLibraryContentCommand(id), cancellationToken));

    [HttpPost("content/{id:guid}/archive")]
    [ProducesResponseType(typeof(LibraryAdminContentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArchiveContent(Guid id, CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(new ArchiveLibraryContentCommand(id), cancellationToken));

    private async Task<Result<StoredFile>> SaveCoverAsync(IFormFile? file, CancellationToken cancellationToken)
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
        return await fileStorage.SaveAsync(stream, file.ContentType, file.Length, "library", cancellationToken);
    }

    private static bool HasMatchingImageSignature(string contentType, ReadOnlySpan<byte> bytes) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };

    private static bool TryParseContentRequest(
        ILibraryContentFormRequest request,
        out Guid? categoryId,
        out LibraryContentType? contentType,
        out IReadOnlyList<LibraryContentSectionInput>? sections,
        out string parseError)
    {
        categoryId = null;
        contentType = null;
        sections = null;
        parseError = string.Empty;

        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            if (!Guid.TryParse(request.CategoryId, out Guid parsedCategoryId))
            {
                parseError = "Cms.Library.InvalidCategory";
                return false;
            }

            categoryId = parsedCategoryId;
        }

        if (string.IsNullOrWhiteSpace(request.Type) ||
            !Enum.TryParse(request.Type, ignoreCase: true, out LibraryContentType parsedType) ||
            !Enum.IsDefined(parsedType))
        {
            parseError = "Cms.Library.InvalidType";
            return false;
        }

        contentType = parsedType;

        if (request.SectionsJson is not null)
        {
            if (!TryParseSections(request.SectionsJson, out sections))
            {
                parseError = "Cms.Library.InvalidSections";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSections(
        string json,
        out IReadOnlyList<LibraryContentSectionInput>? sections)
    {
        try
        {
            List<LibraryContentSectionRequest>? requests =
                JsonSerializer.Deserialize<List<LibraryContentSectionRequest>>(json);
            sections = (requests ?? [])
                .Select(item => new LibraryContentSectionInput(
                    item.DisplayOrder,
                    item.ArabicTitle,
                    item.EnglishTitle,
                    item.ArabicBullets,
                    item.EnglishBullets))
                .ToList();
            return true;
        }
        catch (JsonException)
        {
            sections = null;
            return false;
        }
    }
}

internal interface ILibraryContentFormRequest
{
    string? CategoryId { get; }
    string Type { get; }
    string ArabicTitle { get; }
    string EnglishTitle { get; }
    string ArabicSummary { get; }
    string EnglishSummary { get; }
    string? VideoUrl { get; }
    int? DurationSeconds { get; }
    bool IsPremium { get; }
    string? SectionsJson { get; }
}

public sealed record CreateLibraryContentRequest(
    string Type,
    string? CategoryId,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    bool IsPremium,
    string? SectionsJson) : ILibraryContentFormRequest;

public sealed record UpdateLibraryContentRequest(
    string Type,
    string? CategoryId,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    bool IsPremium,
    string? SectionsJson) : ILibraryContentFormRequest;

public sealed record CreateLibraryCategoryRequest(string ArabicName, string EnglishName, bool IsActive = true);

public sealed record UpdateLibraryCategoryRequest(string ArabicName, string EnglishName, bool IsActive);

public sealed record LibraryContentSectionRequest(
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    List<string>? ArabicBullets,
    List<string>? EnglishBullets);
