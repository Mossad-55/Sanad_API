using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record UploadCareHomeProfileMediaCommand(UserId ActorUserId, int ExpectedVersion,
    CareHomeProfileMediaKind Kind, Stream Content, string ContentType, long Length)
    : ICommand<CareHomeProfileMediaResponse>;

public sealed record RemoveCareHomeProfileMediaCommand(UserId ActorUserId, int ExpectedVersion, Guid MediaId)
    : ICommand;

public sealed class UploadCareHomeProfileMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<UploadCareHomeProfileMediaCommand, CareHomeProfileMediaResponse>
{
    private static readonly Error NotFound = new("CareHomes.Facility.NotFound", "Care-home facility was not found.");
    private static readonly Error Conflict = new("CareHomes.Facility.Conflict", "The care-home profile changed; reload it before editing.");
    private static readonly Error InvalidMedia = new("CareHomes.Media.Invalid", "The image or profile media operation is invalid.");

    public async Task<Result<CareHomeProfileMediaResponse>> Handle(UploadCareHomeProfileMediaCommand request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Kind) || request.Length is <= 0 or > 5_242_880 ||
            request.ContentType is not ("image/jpeg" or "image/png"))
            return Result<CareHomeProfileMediaResponse>.Failure(InvalidMedia);

        byte[] bytes = await ReadBoundedAsync(request.Content, request.Length, ct);
        if (!HasMatchingSignature(request.ContentType, bytes))
            return Result<CareHomeProfileMediaResponse>.Failure(InvalidMedia);

        CareHomeFacility? facility = await db.Facilities.Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeProfileMediaResponse>.Failure(NotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeProfileMediaResponse>.Failure(Conflict);

        CareHomeProfileRevision revision;
        try { revision = facility.PrepareProfileMediaRevision(request.ActorUserId, request.ExpectedVersion, DateTime.UtcNow); }
        catch (DomainException) { return Result<CareHomeProfileMediaResponse>.Failure(InvalidMedia); }

        var sameKind = await db.ProfileMedia.Where(x => x.ProfileRevisionId == revision.Id && x.Kind == request.Kind)
            .OrderBy(x => x.Position).ToListAsync(ct);
        if (request.Kind == CareHomeProfileMediaKind.Cover && sameKind.Count != 0 ||
            request.Kind == CareHomeProfileMediaKind.Gallery && sameKind.Count >= 10)
            return Result<CareHomeProfileMediaResponse>.Failure(InvalidMedia);

        using var image = new MemoryStream(bytes, writable: false);
        var saved = await storage.SavePrivateAsync(image, request.ContentType, bytes.LongLength,
            "care-home-media", 5_242_880, ct);
        if (saved.IsFailure)
            return Result<CareHomeProfileMediaResponse>.Failure(saved.Error);

        try
        {
            CareHomeProfileMedia media = CareHomeProfileMedia.Create(revision.Id, request.Kind,
                request.Kind == CareHomeProfileMediaKind.Cover ? 0 : sameKind.Count,
                saved.Value.Key, request.ContentType, bytes.LongLength, DateTime.UtcNow);
            facility.RecordProfileMediaChange(request.ActorUserId, facility.Version, DateTime.UtcNow);
            db.ProfileMedia.Add(media);
            await db.SaveChangesAsync(ct);
            return new CareHomeProfileMediaResponse(media.Id, media.Kind, media.Position, media.ContentType,
                media.Length, media.ProfileRevisionId,
                $"/api/v1/care-homes/facilities/mine/media/{media.Id:D}/file");
        }
        catch (DomainException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeProfileMediaResponse>.Failure(InvalidMedia);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            return Result<CareHomeProfileMediaResponse>.Failure(Conflict);
        }
        catch
        {
            await storage.DeleteAsync(saved.Value.Key, ct);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, long declaredLength, CancellationToken ct)
    {
        using var output = new MemoryStream((int)Math.Min(declaredLength, 5_242_880));
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > 5_242_880)
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

public sealed class RemoveCareHomeProfileMediaCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<RemoveCareHomeProfileMediaCommand>
{
    private static readonly Error NotFound = new("CareHomes.Media.NotFound", "The facility image was not found.");
    private static readonly Error Conflict = new("CareHomes.Facility.Conflict", "The care-home profile changed; reload it before editing.");
    private static readonly Error InvalidMedia = new("CareHomes.Media.Invalid", "The image cannot be removed in the current state.");

    public async Task<Result> Handle(RemoveCareHomeProfileMediaCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result.Failure(NotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result.Failure(Conflict);

        CareHomeProfileMedia? media = await db.ProfileMedia.SingleOrDefaultAsync(x => x.Id == request.MediaId, ct);
        if (media is null || !facility.Revisions.Any(x => x.Id == media.ProfileRevisionId))
            return Result.Failure(NotFound);
        CareHomeProfileRevision? revision = facility.Revisions.SingleOrDefault(x => x.Id == media.ProfileRevisionId);
        if (revision is null || revision.IsFrozen)
            return Result.Failure(InvalidMedia);

        try
        {
            facility.RecordProfileMediaChange(request.ActorUserId, request.ExpectedVersion, DateTime.UtcNow);
            db.ProfileMedia.Remove(media);
            var later = await db.ProfileMedia.Where(x => x.ProfileRevisionId == media.ProfileRevisionId &&
                x.Kind == CareHomeProfileMediaKind.Gallery && x.Position > media.Position).ToListAsync(ct);
            foreach (var item in later)
                item.SetPosition(item.Position - 1);
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
            return Result.Failure(Conflict);
        }
    }
}
