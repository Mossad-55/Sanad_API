using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.API.Controllers;

public sealed record RoomTypeRequest(int ExpectedVersion, string ArabicName, string EnglishName, decimal MonthlyPriceEgp, CareHomeAllocationMode AllocationMode, string? ArabicDescription, string? EnglishDescription, IReadOnlyList<BilingualCareHomeItem>? Amenities = null);
public sealed record RoomTypeAmenityRequest(string Arabic, string English);
public sealed record RoomRequest(int ExpectedVersion, Guid RoomTypeId, string RoomNumber, string? ArabicDisplayName = null, string? EnglishDisplayName = null);
public sealed record BedRequest(int ExpectedVersion, Guid RoomId, string Label);
public sealed record MaintenanceRequest(int ExpectedVersion, CareHomeMaintenanceTarget Target, Guid TargetId, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record MaintenanceAmendRequest(int ExpectedVersion, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record MaintenanceCancelRequest(int ExpectedVersion);
public sealed record AvailabilityRequest(DateOnly StartDate, DateOnly EndDate);

[ApiController]
[Route("api/v1/care-homes/inventory")]
public sealed class CareHomeInventoryController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) => ToActionResult(await sender.Send(new GetMyInventoryQuery(Actor()), ct));
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/room-types")]
    public Task<IActionResult> CreateType(RoomTypeRequest r, CancellationToken ct) => Send(new CreateRoomTypeCommand(Actor(), r.ExpectedVersion, r.ArabicName, r.EnglishName, r.MonthlyPriceEgp, r.AllocationMode, r.ArabicDescription, r.EnglishDescription), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("mine/room-types/{id:guid}")]
    public Task<IActionResult> UpdateType(Guid id, RoomTypeRequest r, CancellationToken ct) => Send(new UpdateRoomTypeCommand(Actor(), r.ExpectedVersion, id, r.ArabicName, r.EnglishName, r.MonthlyPriceEgp, r.AllocationMode, r.ArabicDescription, r.EnglishDescription, r.Amenities ?? []), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/room-types/{id:guid}/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_242_880 + 102_400)]
    public async Task<IActionResult> UploadTypeMedia(Guid id, [FromForm] int expectedVersion, [FromForm] CareHomeRoomTypeMediaKind kind, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequestWithCode("CareHomes.Inventory.MediaInvalid");
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        var result = await sender.Send(new UploadRoomTypeMediaCommand(Actor(), expectedVersion, id, kind, content, file.ContentType.ToLowerInvariant(), file.Length), ct);
        return result.IsFailure ? ToActionResult(result) : StatusCode(StatusCodes.Status201Created, result.Value);
    }
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpDelete("mine/room-types/{id:guid}/media/{mediaId:guid}")]
    public Task<IActionResult> RemoveTypeMedia(Guid id, Guid mediaId, [FromQuery] int expectedVersion, CancellationToken ct) => Send(new RemoveRoomTypeMediaCommand(Actor(), expectedVersion, id, mediaId), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/room-types/{id:guid}/archive")]
    public Task<IActionResult> ArchiveType(Guid id, int expectedVersion, bool restore, CancellationToken ct) => Send(new ArchiveRoomTypeCommand(Actor(), expectedVersion, id, restore), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/rooms")]
    public Task<IActionResult> CreateRoom(RoomRequest r, CancellationToken ct) => Send(new CreateRoomCommand(Actor(), r.ExpectedVersion, r.RoomTypeId, r.RoomNumber), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("mine/rooms/{id:guid}")]
    public Task<IActionResult> RenameRoom(Guid id, RoomRequest r, CancellationToken ct) => Send(new RenameRoomCommand(Actor(), r.ExpectedVersion, id, r.RoomNumber, r.RoomTypeId, r.ArabicDisplayName, r.EnglishDisplayName), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/rooms/{id:guid}/media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5_242_880 + 102_400)]
    public async Task<IActionResult> UploadRoomMedia(Guid id, [FromForm] int expectedVersion, [FromForm] CareHomeRoomMediaKind kind, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequestWithCode("CareHomes.Inventory.MediaInvalid");
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        var result = await sender.Send(new UploadRoomMediaCommand(Actor(), expectedVersion, id, kind, content, file.ContentType.ToLowerInvariant(), file.Length), ct);
        return result.IsFailure ? ToActionResult(result) : StatusCode(StatusCodes.Status201Created, result.Value);
    }
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpDelete("mine/rooms/{id:guid}/media/{mediaId:guid}")]
    public Task<IActionResult> RemoveRoomMedia(Guid id, Guid mediaId, [FromQuery] int expectedVersion, CancellationToken ct) => Send(new RemoveRoomMediaCommand(Actor(), expectedVersion, id, mediaId), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/rooms/{id:guid}/archive")]
    public Task<IActionResult> ArchiveRoom(Guid id, int expectedVersion, bool restore, CancellationToken ct) => Send(new ArchiveRoomCommand(Actor(), expectedVersion, id, restore), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/beds")]
    public Task<IActionResult> CreateBed(BedRequest r, CancellationToken ct) => Send(new CreateBedCommand(Actor(), r.ExpectedVersion, r.RoomId, r.Label), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("mine/beds/{id:guid}")]
    public Task<IActionResult> RenameBed(Guid id, BedRequest r, CancellationToken ct) => Send(new RenameBedCommand(Actor(), r.ExpectedVersion, id, r.Label), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/beds/{id:guid}/archive")]
    public Task<IActionResult> ArchiveBed(Guid id, int expectedVersion, bool restore, CancellationToken ct) => Send(new ArchiveBedCommand(Actor(), expectedVersion, id, restore), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/maintenance")]
    public Task<IActionResult> Maintenance(MaintenanceRequest r, CancellationToken ct) => Send(new CreateMaintenanceBlockCommand(Actor(), r.ExpectedVersion, r.Target, r.TargetId, r.StartDate, r.EndDate, r.Reason), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("mine/maintenance/{id:guid}")]
    public Task<IActionResult> AmendMaintenance(Guid id, MaintenanceAmendRequest r, CancellationToken ct) => Send(new AmendMaintenanceBlockCommand(Actor(), r.ExpectedVersion, id, r.StartDate, r.EndDate, r.Reason, clock.UtcNow), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/maintenance/{id:guid}/cancel")]
    public Task<IActionResult> CancelMaintenance(Guid id, MaintenanceCancelRequest r, CancellationToken ct) => Send(new CancelMaintenanceBlockCommand(Actor(), r.ExpectedVersion, id, clock.UtcNow), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("mine/availability")]
    public Task<IActionResult> MyAvailability(AvailabilityRequest r, CancellationToken ct) => Send(new GetMyAvailabilityQuery(Actor(), r.StartDate, r.EndDate), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
    [HttpGet("admin/{facilityId:guid}")]
    public Task<IActionResult> Admin(Guid facilityId, CancellationToken ct) => Send(new GetAdminInventoryQuery(facilityId), ct);
    [Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
    [HttpPost("admin/{facilityId:guid}/availability")]
    public Task<IActionResult> Availability(Guid facilityId, AvailabilityRequest r, CancellationToken ct) => Send(new GetAvailabilityQuery(facilityId, r.StartDate, r.EndDate), ct);
    private UserId Actor() => new(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
    private async Task<IActionResult> Send<T>(IRequest<Sanad.BuildingBlocks.Application.Results.Result<T>> command, CancellationToken ct) => ToActionResult(await sender.Send(command, ct));
    private async Task<IActionResult> Send(IRequest<Sanad.BuildingBlocks.Application.Results.Result> command, CancellationToken ct) => ToActionResult(await sender.Send(command, ct));
}
