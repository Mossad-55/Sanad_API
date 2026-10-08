using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.CareHomes.Domain.Facilities;

public enum CareHomeProfileMediaKind
{
    Cover = 1,
    Gallery = 2
}

public sealed class CareHomeProfileMedia : Entity<Guid>
{
    private CareHomeProfileMedia() { }

    private CareHomeProfileMedia(Guid id, Guid profileRevisionId, CareHomeProfileMediaKind kind,
        int position, string storageKey, string contentType, long length, DateTime createdOnUtc) : base(id)
    {
        ProfileRevisionId = profileRevisionId;
        Kind = kind;
        Position = position;
        StorageKey = storageKey;
        ContentType = contentType;
        Length = length;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid ProfileRevisionId { get; private set; }
    public CareHomeProfileMediaKind Kind { get; private set; }
    public int Position { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public void SetPosition(int position)
    {
        if (Kind != CareHomeProfileMediaKind.Gallery || position is < 0 or > 9)
            throw new DomainException("Care-home gallery position is invalid.");
        Position = position;
    }

    public static CareHomeProfileMedia Create(Guid profileRevisionId, CareHomeProfileMediaKind kind,
        int position, string storageKey, string contentType, long length, DateTime utcNow)
    {
        if (profileRevisionId == Guid.Empty || !Enum.IsDefined(kind))
            throw new DomainException("Care-home profile media target is invalid.");
        if (position < 0 || kind == CareHomeProfileMediaKind.Cover && position != 0 ||
            kind == CareHomeProfileMediaKind.Gallery && position > 9)
            throw new DomainException("Care-home profile media position is invalid.");
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("private/care-home-media/", StringComparison.Ordinal))
            throw new DomainException("Care-home profile media must use private storage.");
        if (contentType is not ("image/jpeg" or "image/png") || length is <= 0 or > 5_242_880)
            throw new DomainException("Care-home images must be JPG or PNG and no larger than 5 MB.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-home media timestamps must be UTC.");
        return new CareHomeProfileMedia(Guid.CreateVersion7(), profileRevisionId, kind, position,
            storageKey.Trim(), contentType, length, utcNow);
    }
}
