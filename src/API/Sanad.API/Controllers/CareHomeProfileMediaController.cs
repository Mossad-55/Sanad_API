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
public sealed class CareHomeProfileMediaController(ISender sender, CareHomesDbContext db, IFileStorage storage) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("api/v1/care-homes/facilities/mine/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_242_880 + 102_400)]
    public async Task<IActionResult> Upload([FromForm] int expectedVersion, [FromForm] CareHomeProfileMediaKind kind,
        [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (!TryActor(out UserId actor)) return Unauthorized();
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
    public async Task<IActionResult> Remove(Guid mediaId, [FromQuery] int expectedVersion, CancellationToken ct)
    {
        if (!TryActor(out UserId actor)) return Unauthorized();
        var result = await sender.Send(new RemoveCareHomeProfileMediaCommand(actor, expectedVersion, mediaId), ct);
        return result.IsFailure ? BadRequest(result.Error) : NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("api/v1/care-homes/facilities/mine/media/{mediaId:guid}/file")]
    public async Task<IActionResult> ReadOwner(Guid mediaId, CancellationToken ct)
    {
        if (!TryActor(out UserId actor)) return Unauthorized();
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await db.Facilities.AsNoTracking().AnyAsync(f => f.OwnerUserId == actor &&
            f.Revisions.Any(r => r.Id == item.ProfileRevisionId), ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
    [HttpGet("api/v1/admin/care-homes/{careHomeId:guid}/media/{mediaId:guid}/file")]
    public async Task<IActionResult> ReadAdmin(Guid careHomeId, Guid mediaId, CancellationToken ct)
    {
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await db.Facilities.AsNoTracking().AnyAsync(f => f.Id.Value == careHomeId &&
            f.Revisions.Any(r => r.Id == item.ProfileRevisionId), ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [AllowAnonymous]
    [HttpGet("api/v1/care-homes/discovery/{careHomeId:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> ReadApproved(Guid careHomeId, Guid mediaId, CancellationToken ct)
    {
        var item = await db.ProfileMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (item is not null && !await db.Facilities.AsNoTracking().AnyAsync(f => f.Id.Value == careHomeId &&
            f.Status == CareHomeStatus.Approved && f.ApprovedRevisionId == item.ProfileRevisionId, ct)) item = null;
        if (item is null) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    private async Task<IActionResult> Open(string key, CancellationToken ct)
    {
        var result = await storage.OpenReadAsync(key, ct);
        if (result.IsFailure) return NotFound();
        return File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false);
    }

    private bool TryActor(out UserId actor)
    {
        string? value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(value, out Guid id)) { actor = new UserId(id); return true; }
        actor = UserId.Empty;
        return false;
    }
}
