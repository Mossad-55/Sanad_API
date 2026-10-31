using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Inventory;

public sealed class UploadRoomTypeMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<UploadRoomTypeMediaCommand, CareHomeRoomTypeMediaDto>
{
    private const long MaximumLength = 5_242_880;
    private static readonly Error NotFound = new("CareHomes.Inventory.NotFound", "Room type was not found.");
    private static readonly Error Conflict = new("CareHomes.Inventory.Conflict", "Inventory changed; reload before editing.");
    private static readonly Error InvalidMedia = new("CareHomes.Inventory.MediaInvalid", "The image is not supported by this room type.");

    public async Task<Result<CareHomeRoomTypeMediaDto>> Handle(UploadRoomTypeMediaCommand r, CancellationToken ct)
    {
        if (!Enum.IsDefined(r.Kind) || r.Length is <= 0 or > MaximumLength || r.ContentType is not ("image/jpeg" or "image/png"))
            return Result<CareHomeRoomTypeMediaDto>.Failure(InvalidMedia);

        byte[] bytes = await ReadBoundedAsync(r.Content, r.Length, ct);
        if (!HasMatchingSignature(r.ContentType, bytes))
            return Result<CareHomeRoomTypeMediaDto>.Failure(InvalidMedia);

        var (f, fail) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct);
        if (fail is not null) return Result<CareHomeRoomTypeMediaDto>.Failure(fail.Error);
        var type = await db.RoomTypes.SingleOrDefaultAsync(x => x.Id == r.RoomTypeId && x.FacilityId == f!.Id, ct);
        if (type is null) return Result<CareHomeRoomTypeMediaDto>.Failure(NotFound);

        var sameKind = await db.RoomTypeMedia.Where(x => x.RoomTypeId == type.Id && x.Kind == r.Kind)
            .OrderBy(x => x.Position).ToListAsync(ct);
        if (r.Kind == CareHomeRoomTypeMediaKind.Cover && sameKind.Count != 0 ||
            r.Kind == CareHomeRoomTypeMediaKind.Gallery && sameKind.Count >= 10)
            return Result<CareHomeRoomTypeMediaDto>.Failure(InvalidMedia);

        using var image = new MemoryStream(bytes, writable: false);
        var saved = await storage.SavePrivateAsync(image, r.ContentType, bytes.LongLength, "care-home-media", MaximumLength, ct);
        if (saved.IsFailure)
            return Result<CareHomeRoomTypeMediaDto>.Failure(saved.Error);

        try
        {
            CareHomeRoomTypeMedia media = CareHomeRoomTypeMedia.Create(type.Id, r.Kind,
                r.Kind == CareHomeRoomTypeMediaKind.Cover ? 0 : sameKind.Count,
                saved.Value.Key, r.ContentType, bytes.LongLength, DateTime.UtcNow);
            db.RoomTypeMedia.Add(media);
            f!.RecordInventoryChange(DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            return Result<CareHomeRoomTypeMediaDto>.Success(ToDto(f!.Id.Value, type.Id, media));
        }
        catch (DomainException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeRoomTypeMediaDto>.Failure(InvalidMedia);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeRoomTypeMediaDto>.Failure(Conflict);
        }
        catch
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            throw;
        }
    }

    internal static CareHomeRoomTypeMediaDto ToDto(Guid facilityId, Guid roomTypeId, CareHomeRoomTypeMedia media) =>
        new(media.Id, media.Kind, media.Position, media.ContentType, media.Length,
            media.Kind == CareHomeRoomTypeMediaKind.Cover
                ? $"/api/v1/care-homes/discovery/{facilityId:D}/room-types/{roomTypeId:D}/media/{media.Id:D}/file"
                : null);

    private static async Task<byte[]> ReadBoundedAsync(Stream input, long declaredLength, CancellationToken ct)
    {
        using var output = new MemoryStream((int)Math.Min(declaredLength, MaximumLength));
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaximumLength)
                return [];
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.Length == declaredLength ? output.ToArray() : [];
    }

    private static bool HasMatchingSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false
    };
}

public sealed class RemoveRoomTypeMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<RemoveRoomTypeMediaCommand>
{
    private static readonly Error NotFound = new("CareHomes.Inventory.MediaNotFound", "The room type image was not found.");
    private static readonly Error InvalidMedia = new("CareHomes.Inventory.MediaInvalid", "The image cannot be removed from this room type.");

    public async Task<Result> Handle(RemoveRoomTypeMediaCommand r, CancellationToken ct)
    {
        var (f, fail) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct);
        if (fail is not null) return Result.Failure(fail.Error);
        CareHomeRoomTypeMedia? media = await db.RoomTypeMedia.SingleOrDefaultAsync(x => x.Id == r.MediaId && x.RoomTypeId == r.RoomTypeId, ct);
        if (media is null) return Result.Failure(NotFound);
        if (!await db.RoomTypes.AnyAsync(x => x.Id == r.RoomTypeId && x.FacilityId == f!.Id, ct)) return Result.Failure(NotFound);

        try
        {
            db.RoomTypeMedia.Remove(media);
            var later = await db.RoomTypeMedia.Where(x => x.RoomTypeId == media.RoomTypeId &&
                x.Kind == CareHomeRoomTypeMediaKind.Gallery && x.Position > media.Position).ToListAsync(ct);
            foreach (var item in later)
                item.SetPosition(item.Position - 1);
            f!.RecordInventoryChange(DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            await storage.DeleteAsync(media.StorageKey, ct);
            return Result.Success();
        }
        catch (DomainException)
        {
            return Result.Failure(InvalidMedia);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("CareHomes.Inventory.Conflict", "Inventory changed; reload before editing."));
        }
    }
}

// A room type becomes bookable only once it publishes a cover plus at least one
// gallery photo; the gate is checked when a checkout hold is created.
internal static class RoomTypeBookability
{
    internal static Error RoomMediaRequired => new("CareHomes.Inventory.RoomMediaRequired", "The selected room type is not published with a cover photo and at least one gallery photo.");
    internal static bool HasRequiredMedia(IEnumerable<CareHomeRoomTypeMedia> media, Guid roomTypeId) =>
        media.Any(x => x.RoomTypeId == roomTypeId && x.Kind == CareHomeRoomTypeMediaKind.Cover) &&
        media.Any(x => x.RoomTypeId == roomTypeId && x.Kind == CareHomeRoomTypeMediaKind.Gallery);
}
