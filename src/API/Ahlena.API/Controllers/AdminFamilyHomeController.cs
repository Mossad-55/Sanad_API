using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ahlena.API.Authorization;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Cms.Application.Landing;
using Ahlena.Modules.Cms.Domain.Landing;
using System.IO;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/family-home/sections")]
public sealed class AdminFamilyHomeController(
    ISender sender,
    IOptions<LocalStorageOptions> storageOptions) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(FamilyHomeAdminPageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSections(
        [FromQuery] string? status = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        FamilyHomeSectionStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!FamilyHomeKindParser.TryParseStatus(status, out FamilyHomeSectionStatus parsedStatus))
                return BadRequestWithCode(FamilyHomeErrors.SectionInvalid.Code);

            statusFilter = parsedStatus;
        }

        FamilyHomeSectionKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!FamilyHomeKindParser.TryParseKind(kind, out FamilyHomeSectionKind parsedKind))
                return BadRequestWithCode(FamilyHomeErrors.InvalidKind.Code);

            kindFilter = parsedKind;
        }

        return this.ToFamilyHomeActionResult(await sender.Send(
            new GetFamilyHomeAdminListQuery(statusFilter, kindFilter, search, page, pageSize),
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FamilyHomeAdminSectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSection(Guid id, CancellationToken cancellationToken = default) =>
        this.ToFamilyHomeActionResult(await sender.Send(
            new GetFamilyHomeAdminSectionQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(FamilyHomeAdminSectionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSection(
        [FromBody] CreateFamilyHomeSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!FamilyHomeKindParser.TryParseKind(request.Kind, out FamilyHomeSectionKind kind))
            return BadRequestWithCode(FamilyHomeErrors.InvalidKind.Code);

        if (!RequestMediaKeysAreValid(request.Items))
            return BadRequestWithCode(FamilyHomeErrors.SectionInvalid.Code);

        var result = await sender.Send(new CreateFamilyHomeSectionCommand(
            kind,
            request.Items,
            request.DisplayOrder,
            request.IsVisible), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToFamilyHomeActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FamilyHomeAdminSectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSection(
        Guid id,
        [FromBody] UpdateFamilyHomeSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!RequestMediaKeysAreValid(request.Items))
            return BadRequestWithCode(FamilyHomeErrors.SectionInvalid.Code);

        return this.ToFamilyHomeActionResult(await sender.Send(
            new UpdateFamilyHomeSectionCommand(
                id,
                request.Items,
                request.DisplayOrder,
                request.IsVisible,
                request.ExpectedVersion), cancellationToken));
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(FamilyHomeAdminSectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishSection(
        Guid id,
        [FromBody] PublishFamilyHomeSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        // Publication validates the stored payload, not just the request: the
        // draft may have been written before its media moved. The Admin detail
        // read is the typed view of exactly what is about to go live.
        Result<FamilyHomeAdminSectionResponse> stored = await sender.Send(
            new GetFamilyHomeAdminSectionQuery(id), cancellationToken);
        if (stored.IsFailure)
            return this.ToFamilyHomeActionResult(stored);

        if (!StoredMediaKeysAreValid(stored.Value.Items))
            return BadRequestWithCode(FamilyHomeErrors.SectionInvalid.Code);

        return this.ToFamilyHomeActionResult(await sender.Send(
            new PublishFamilyHomeSectionCommand(id, actorUserId.Value, request.ExpectedVersion),
            cancellationToken));
    }

    [HttpPost("{id:guid}/visibility")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetVisibility(
        Guid id,
        [FromBody] SetFamilyHomeSectionVisibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        return this.ToFamilyHomeActionResult(await sender.Send(
            new SetFamilyHomeSectionVisibilityCommand(
                id, request.Value, actorUserId.Value, request.ExpectedVersion),
            cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSection(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId))
            return Unauthorized();

        return this.ToFamilyHomeActionResult(await sender.Send(
            new DeleteFamilyHomeSectionCommand(id, actorUserId.Value), cancellationToken));
    }

    [HttpPost("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReorderSections(
        [FromBody] ReorderFamilyHomeSectionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ReorderFamilyHomeSectionsCommand(
            (request.Items ?? [])
                .Select(item => new FamilyHomeReorderItem(item.Id, item.DisplayOrder))
                .ToList()), cancellationToken);

        return this.ToFamilyHomeActionResult(result);
    }

    private bool RequestMediaKeysAreValid(IReadOnlyList<FamilyHomeItemRequest>? items)
    {
        foreach (FamilyHomeItemRequest item in items ?? [])
        {
            if (item.ImageKey is { } imageKey &&
                !string.IsNullOrWhiteSpace(imageKey) &&
                !IsValidPublicMediaKey(imageKey))
            {
                return false;
            }

            if (item.IconKey is { } iconKey &&
                !string.IsNullOrWhiteSpace(iconKey) &&
                !IsValidPublicMediaKey(iconKey))
            {
                return false;
            }
        }

        return true;
    }

    private bool StoredMediaKeysAreValid(IReadOnlyList<FamilyHomeItem> items)
    {
        foreach (FamilyHomeItem item in items)
        {
            if (item.ImageKey is { } imageKey && !IsValidPublicMediaKey(imageKey))
                return false;

            if (item.IconKey is { } iconKey && !IsValidPublicMediaKey(iconKey))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A valid family home media key names a public file: it never selects the
    /// private sibling directory, it normalizes to a path that stays inside the
    /// effective public storage root, and it exists on disk.
    /// </summary>
    private bool IsValidPublicMediaKey(string key)
    {
        if (key.StartsWith("private/", StringComparison.OrdinalIgnoreCase))
            return false;

        string root = storageOptions.Value.GetEffectiveRootPath();
        if (string.IsNullOrWhiteSpace(root))
            return false;

        string normalized = key.Replace('\\', '/').TrimStart('/');
        string fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        string fullRoot = Path.GetFullPath(root);

        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        return System.IO.File.Exists(fullPath);
    }
}

/// <summary>
/// The family home phase pins its own status codes instead of editing the shared
/// <see cref="ResultProblemDetailsMapper"/> (which parallel lanes also touch):
/// any unmapped code there defaults to 400, while this surface needs a missing
/// section at 404 and stale version or retained history at 409. Both family home
/// controllers use these helpers, and the response keeps the mapper's
/// <c>code</c> and <c>traceId</c> extensions so a client can key on the code.
/// </summary>
public static class FamilyHomeResultMapping
{
    private static readonly IReadOnlyDictionary<string, int> StatusCodesByFamilyHomeErrorCode =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Cms.FamilyHome.SectionInvalid"] = 400,
            ["Cms.FamilyHome.InvalidKind"] = 400,
            ["Cms.FamilyHome.Incomplete"] = 400,
            ["Cms.FamilyHome.ReorderInvalid"] = 400,
            ["Cms.FamilyHome.SectionNotFound"] = 404,
            ["Cms.FamilyHome.StaleVersion"] = 409,
            ["Cms.FamilyHome.PublishedHistoryConflict"] = 409,
            ["Cms.FamilyHome.ImmutableHistory"] = 409
        };

    public static IActionResult ToFamilyHomeActionResult(
        this ApiControllerBase controller,
        Result result) =>
        result.IsSuccess
            ? controller.NoContent()
            : Respond(controller, result.Error);

    public static IActionResult ToFamilyHomeActionResult<T>(
        this ApiControllerBase controller,
        Result<T> result) =>
        result.IsSuccess
            ? controller.Ok(result.Value)
            : Respond(controller, result.Error);

    private static IActionResult Respond(ApiControllerBase controller, Error error)
    {
        ProblemDetails problemDetails = ResultProblemDetailsMapper.Create(
            error, controller.HttpContext);

        if (StatusCodesByFamilyHomeErrorCode.TryGetValue(error.Code, out int statusCode))
        {
            problemDetails.Type = $"https://httpstatuses.com/{statusCode}";
            problemDetails.Status = statusCode;
            problemDetails.Title = statusCode switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Bad Request"
            };
            problemDetails.Detail = statusCode switch
            {
                StatusCodes.Status404NotFound => "The requested resource was not found.",
                StatusCodes.Status409Conflict => "The request conflicts with the current state.",
                _ => "The request could not be completed."
            };
        }

        return controller.StatusCode(
            problemDetails.Status ?? StatusCodes.Status500InternalServerError,
            problemDetails);
    }
}
