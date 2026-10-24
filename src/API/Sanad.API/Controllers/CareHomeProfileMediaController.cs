using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.API.Controllers;

[ApiController]
public sealed class CareHomeProfileMediaController(ISender sender, CareHomesDbContext db, IFileStorage storage) : ApiControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("api/v1/care-homes/facilities/mine/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_242_880 + 102_400)]
    [ProducesResponseType(typeof(CareHomeProfileMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upload([FromForm] int expectedVersion, [FromForm] CareHomeProfileMediaKind kind,
        [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        if (file is null) return BadRequest();
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        var result = await sender.Send(new UploadCareHomeProfileMediaCommand(actor, expectedVersion, kind,
            content, file.ContentType.ToLowerInvariant(), file.Length), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(result.Value);
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpDelete("api/v1/care-homes/facilities/mine/media/{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(Guid mediaId, [FromQuery] int expectedVersion, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var result = await sender.Send(new RemoveCareHomeProfileMediaCommand(actor, expectedVersion, mediaId), ct);
        return result.IsFailure ? BadRequest(result.Error) : NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("api/v1/care-homes/facilities/mine/media/{mediaId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReadOwner(Guid mediaId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await OwnsRevisionAsync(actor, item.ProfileRevisionId, ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
    [HttpGet("api/v1/admin/care-homes/{careHomeId:guid}/media/{mediaId:guid}/file")]
    public async Task<IActionResult> ReadAdmin(Guid careHomeId, Guid mediaId, CancellationToken ct)
    {
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await FacilityOwnsRevisionAsync(careHomeId, item.ProfileRevisionId, ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [AllowAnonymous]
    [HttpGet("api/v1/care-homes/discovery/{careHomeId:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> ReadApproved(Guid careHomeId, Guid mediaId, CancellationToken ct)
    {
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await IsApprovedRevisionAsync(careHomeId, item.ProfileRevisionId, ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    private async Task<IActionResult> Open(string key, CancellationToken ct)
    {
        var result = await storage.OpenReadAsync(key, ct);
        if (result.IsFailure) return NotFound();
        return File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: true);
    }

    private async Task<bool> OwnsRevisionAsync(UserId actor, Guid revisionId, CancellationToken ct)
    {
        List<Guid> revisionIds = await db.Facilities.AsNoTracking()
            .Where(f => f.OwnerUserId == actor)
            .SelectMany(f => f.Revisions.Select(r => r.Id))
            .ToListAsync(ct);
        return revisionIds.Contains(revisionId);
    }

    private async Task<bool> FacilityOwnsRevisionAsync(Guid careHomeId, Guid revisionId, CancellationToken ct)
    {
        List<Guid> revisionIds = await db.Facilities.AsNoTracking()
            .Where(f => f.Id == new CareHomeId(careHomeId))
            .SelectMany(f => f.Revisions.Select(r => r.Id))
            .ToListAsync(ct);
        return revisionIds.Contains(revisionId);
    }

    private async Task<bool> IsApprovedRevisionAsync(Guid careHomeId, Guid revisionId, CancellationToken ct)
    {
        Guid? approvedRevisionId = await db.Facilities.AsNoTracking()
            .Where(f => f.Id == new CareHomeId(careHomeId) && f.Status == CareHomeStatus.Approved)
            .Select(f => f.ApprovedRevisionId)
            .SingleOrDefaultAsync(ct);
        return approvedRevisionId == revisionId;
    }

}
