using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Inventory;

// Per-room photo upload/remove mirrors the room type media handlers byte-for-byte:
// same content sniffing, same 5 MB cap, same 1 cover + <=10 gallery slots, same
// cleanup-on-failure and gallery re-ordering semantics.
public sealed class UploadRoomMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<UploadRoomMediaCommand, CareHomeRoomMediaDto>
{
    private const long MaximumLength = 5_242_880;
    private static readonly Error NotFound = new("CareHomes.Inventory.NotFound", "Room was not found.");
    private static readonly Error Conflict = new("CareHomes.Inventory.Conflict", "Inventory changed; reload before editing.");
    private static readonly Error InvalidMedia = new("CareHomes.Inventory.MediaInvalid", "The image is not supported by this room.");

    public async Task<Result<CareHomeRoomMediaDto>> Handle(UploadRoomMediaCommand r, CancellationToken ct)
    {
        if (!Enum.IsDefined(r.Kind) || r.Length is <= 0 or > MaximumLength || r.ContentType is not ("image/jpeg" or "image/png"))
            return Result<CareHomeRoomMediaDto>.Failure(InvalidMedia);

        byte[] bytes = await ReadBoundedAsync(r.Content, r.Length, ct);
        if (!HasMatchingSignature(r.ContentType, bytes))
            return Result<CareHomeRoomMediaDto>.Failure(InvalidMedia);

        var (f, fail) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct);
        if (fail is not null) return Result<CareHomeRoomMediaDto>.Failure(fail.Error);
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == r.RoomId && x.FacilityId == f!.Id, ct);
        if (room is null) return Result<CareHomeRoomMediaDto>.Failure(NotFound);

        var sameKind = await db.RoomMedia.Where(x => x.RoomId == room.Id && x.Kind == r.Kind)
            .OrderBy(x => x.Position).ToListAsync(ct);
        if (r.Kind == CareHomeRoomMediaKind.Cover && sameKind.Count != 0 ||
            r.Kind == CareHomeRoomMediaKind.Gallery && sameKind.Count >= 10)
            return Result<CareHomeRoomMediaDto>.Failure(InvalidMedia);

        using var image = new MemoryStream(bytes, writable: false);
        var saved = await storage.SavePrivateAsync(image, r.ContentType, bytes.LongLength, "care-home-media", MaximumLength, ct);
        if (saved.IsFailure)
            return Result<CareHomeRoomMediaDto>.Failure(saved.Error);

        try
        {
            CareHomeRoomMedia media = CareHomeRoomMedia.Create(room.Id, r.Kind,
                r.Kind == CareHomeRoomMediaKind.Cover ? 0 : sameKind.Count,
                saved.Value.Key, r.ContentType, bytes.LongLength, DateTime.UtcNow);
            db.RoomMedia.Add(media);
            f!.RecordInventoryChange(DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            return Result<CareHomeRoomMediaDto>.Success(ToDto(f!.Id.Value, room.Id, media));
        }
        catch (DomainException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeRoomMediaDto>.Failure(InvalidMedia);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeRoomMediaDto>.Failure(Conflict);
        }
        catch
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            throw;
        }
    }

    internal static CareHomeRoomMediaDto ToDto(Guid facilityId, Guid roomId, CareHomeRoomMedia media) =>
        new(media.Id, media.Kind, media.Position, media.ContentType, media.Length,
            media.Kind == CareHomeRoomMediaKind.Cover
                ? $"/api/v1/care-homes/discovery/{facilityId:D}/rooms/{roomId:D}/media/{media.Id:D}/file"
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

public sealed class RemoveRoomMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<RemoveRoomMediaCommand>
{
    private static readonly Error NotFound = new("CareHomes.Inventory.MediaNotFound", "The room image was not found.");
    private static readonly Error InvalidMedia = new("CareHomes.Inventory.MediaInvalid", "The image cannot be removed from this room.");

    public async Task<Result> Handle(RemoveRoomMediaCommand r, CancellationToken ct)
    {
        var (f, fail) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct);
        if (fail is not null) return Result.Failure(fail.Error);
        CareHomeRoomMedia? media = await db.RoomMedia.SingleOrDefaultAsync(x => x.Id == r.MediaId && x.RoomId == r.RoomId, ct);
        if (media is null) return Result.Failure(NotFound);
        if (!await db.Rooms.AnyAsync(x => x.Id == r.RoomId && x.FacilityId == f!.Id, ct)) return Result.Failure(NotFound);

        try
        {
            db.RoomMedia.Remove(media);
            var later = await db.RoomMedia.Where(x => x.RoomId == media.RoomId &&
                x.Kind == CareHomeRoomMediaKind.Gallery && x.Position > media.Position).ToListAsync(ct);
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
