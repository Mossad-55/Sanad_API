using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.API.Controllers;

[Authorize]
[Route("api/v1/library")]
public sealed class LibraryController : ApiControllerBase
{
    private readonly ISender _sender;

    public LibraryController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(new GetLibraryCategoriesQuery(), cancellationToken));
    }

    [HttpGet("content")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryContentListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContent(
        [FromQuery] string? type = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
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

        var contentSort = LibraryContentSort.Newest;
        if (!string.IsNullOrWhiteSpace(sort) &&
            !Enum.TryParse(sort, ignoreCase: true, out contentSort))
        {
            return BadRequestWithCode("Cms.Library.InvalidSort");
        }

        var query = new GetLibraryContentListQuery(
            contentType, categoryId, search, contentSort, page, pageSize, userId.Value);
        var result = await _sender.Send(query, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpGet("content/{id:guid}")]
    [ProducesResponseType(typeof(LibraryContentDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContentDetail(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        return ToActionResult(await _sender.Send(
            new GetLibraryContentQuery(id, userId.Value), cancellationToken));
    }

    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryContentListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] Guid? contentId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetLibraryRecommendationsQuery(
            contentId, categoryId, page, pageSize, userId.Value);
        var result = await _sender.Send(query, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpGet("saved")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryContentListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSaved(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetSavedLibraryContentQuery(userId.Value, page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPut("content/{id:guid}/saved")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetSaved(
        Guid id,
        [FromBody] SetLibraryValueRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SetLibraryContentSavedCommand(id, userId.Value, request.Value), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPut("content/{id:guid}/liked")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetLiked(
        Guid id,
        [FromBody] SetLibraryValueRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SetLibraryContentLikedCommand(id, userId.Value, request.Value), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("content/{id:guid}/view")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordView(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new RecordLibraryContentViewCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }
}

public sealed record SetLibraryValueRequest(bool Value);
