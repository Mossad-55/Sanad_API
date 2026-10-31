using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.API.Controllers;

// Room and room-type photo file serving (owner-approved decision 3a).
// Mirrors CareHomeProfileMediaController: every mismatch collapses to 404
// (no existence oracle); storage misses also surface as 404.
[ApiController]
public sealed class CareHomeRoomMediaController(CareHomesDbContext db, IFileStorage storage) : ApiControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("api/v1/care-homes/inventory/mine/room-types/{id:guid}/media/{mediaId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReadOwnerTypeFile(Guid id, Guid mediaId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var item = await db.RoomTypeMedia.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaId && x.RoomTypeId == id, ct);
        var type = item is null ? null : await db.RoomTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null || type is null || !await db.Facilities.AsNoTracking()
            .AnyAsync(f => f.Id == type.FacilityId && f.OwnerUserId == actor, ct)) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("api/v1/care-homes/inventory/mine/rooms/{id:guid}/media/{mediaId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReadOwnerRoomFile(Guid id, Guid mediaId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized();
        var item = await db.RoomMedia.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaId && x.RoomId == id, ct);
        var room = item is null ? null : await db.Rooms.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (item is null || room is null || !await db.Facilities.AsNoTracking()
            .AnyAsync(f => f.Id == room.FacilityId && f.OwnerUserId == actor, ct)) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [AllowAnonymous]
    [HttpGet("api/v1/care-homes/discovery/{careHomeId:guid}/room-types/{roomTypeId:guid}/media/{mediaId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadRoomTypeFile(Guid careHomeId, Guid roomTypeId, Guid mediaId, CancellationToken ct)
    {
        var type = await db.RoomTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == roomTypeId && !t.IsArchived, ct);
        if (type is null || type.FacilityId.Value != careHomeId) return NotFound();
        var item = await db.RoomTypeMedia.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaId && x.RoomTypeId == roomTypeId, ct);
        if (item is null || !await db.Facilities.AsNoTracking()
            .AnyAsync(f => f.Id == new CareHomeId(careHomeId) && f.Status == CareHomeStatus.Approved, ct)) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    [AllowAnonymous]
    [HttpGet("api/v1/care-homes/discovery/{careHomeId:guid}/rooms/{roomId:guid}/media/{mediaId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadRoomFile(Guid careHomeId, Guid roomId, Guid mediaId, CancellationToken ct)
    {
        var room = await db.Rooms.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == roomId && !r.IsArchived, ct);
        if (room is null || room.FacilityId.Value != careHomeId) return NotFound();
        if (!await db.RoomTypes.AsNoTracking()
            .AnyAsync(t => t.Id == room.RoomTypeId && !t.IsArchived, ct)) return NotFound();
        var item = await db.RoomMedia.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaId && x.RoomId == roomId, ct);
        if (item is null || !await db.Facilities.AsNoTracking()
            .AnyAsync(f => f.Id == new CareHomeId(careHomeId) && f.Status == CareHomeStatus.Approved, ct)) return NotFound();
        return await Open(item.StorageKey, ct);
    }

    private async Task<IActionResult> Open(string key, CancellationToken ct)
    {
        var result = await storage.OpenReadAsync(key, ct);
        if (result.IsFailure) return NotFound();
        return File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: true);
    }
}
