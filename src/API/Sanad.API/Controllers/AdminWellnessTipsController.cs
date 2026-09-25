using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Wellness;
using Sanad.Modules.Cms.Domain.Wellness;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/wellness-tips")]
public sealed class AdminWellnessTipsController(ISender sender, IFileStorage fileStorage) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] WellnessTipPublicationStatus? status, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => ToActionResult(await sender.Send(new ListWellnessTipsQuery(status, category, search, Page: page, PageSize: pageSize), ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new GetWellnessTipQuery(id), ct));
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new GetWellnessTipQuery(id), ct));
    [HttpPost]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] CreateWellnessTipRequest request, IFormFile? file, CancellationToken ct)
    {
        if (!TryMap(request.SectionsJson, out var sections)) return BadRequest();
        var upload = await SaveImageAsync(file, ct);
        if (upload.IsFailure) return ToActionResult(upload);
        var result = await sender.Send(new CreateWellnessTipCommand(request.ArabicTitle, request.EnglishTitle, request.Category, upload.Value.Key, sections), ct);
        if (result.IsFailure) await fileStorage.DeleteAsync(upload.Value.Key, ct);
        return result.IsFailure ? ToActionResult(result) : StatusCode(StatusCodes.Status201Created, result.Value);
    }
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateWellnessTipRequest request, IFormFile? file, CancellationToken ct)
    {
        IReadOnlyList<WellnessTipSectionInput>? sections = null;
        if (request.SectionsJson is not null && !TryMap(request.SectionsJson, out sections)) return BadRequest();
        string? imageKey = null;
        if (file is not null)
        {
            var upload = await SaveImageAsync(file, ct);
            if (upload.IsFailure) return ToActionResult(upload);
            imageKey = upload.Value.Key;
        }
        var result = await sender.Send(new UpdateWellnessTipCommand(id, request.ArabicTitle, request.EnglishTitle, request.Category, imageKey, sections), ct);
        if (result.IsFailure && imageKey is not null) await fileStorage.DeleteAsync(imageKey, ct);
        return ToActionResult(result);
    }
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new PublishWellnessTipCommand(id), ct));
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) => ToActionResult(await sender.Send(new ArchiveWellnessTipCommand(id), ct));
    private async Task<Result<StoredFile>> SaveImageAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return Result<StoredFile>.Failure(StorageErrors.Empty);
        if (file.Length <= 0) return Result<StoredFile>.Failure(StorageErrors.Empty);
        if (file.Length > 5_242_880) return Result<StoredFile>.Failure(StorageErrors.TooLarge);
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        if (!HasMatchingImageSignature(file.ContentType, stream.GetBuffer().AsSpan(0, (int)stream.Length)))
            return Result<StoredFile>.Failure(StorageErrors.UnsupportedType);
        stream.Position = 0;
        return await fileStorage.SaveAsync(stream, file.ContentType, file.Length, "wellness-tips", ct);
    }

    private static bool HasMatchingImageSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };

    private static bool TryMap(string? json, out IReadOnlyList<WellnessTipSectionInput> sections)
    {
        try
        {
            var requests = JsonSerializer.Deserialize<List<WellnessTipSectionRequest>>(json ?? "[]");
            sections = (requests ?? []).Select(x => new WellnessTipSectionInput(x.DisplayOrder, x.ArabicText, x.EnglishText)).ToList();
            return true;
        }
        catch (JsonException)
        {
            sections = [];
            return false;
        }
    }
}
