using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Domain.Uploads;

public sealed class CommunityImage : AggregateRoot<CommunityImageId>
{
    public const int MaximumStorageKeyLength = 500;

    private CommunityImage()
    {
    }

    private CommunityImage(
        CommunityImageId id,
        string storageKey,
        string contentType,
        long sizeBytes,
        UserId uploadedBy,
        DateTime uploadedOnUtc)
        : base(id)
    {
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy;
        UploadedOnUtc = uploadedOnUtc;
    }

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public UserId UploadedBy { get; private set; } = default!;

    public DateTime UploadedOnUtc { get; private set; }

    public static CommunityImage Create(
        string storageKey,
        string contentType,
        long sizeBytes,
        UserId uploadedBy,
        DateTime uploadedOnUtc)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new DomainException(
                "Image storage key is required.");
        }

        string normalizedKey = storageKey.Trim();

        if (normalizedKey.Length > MaximumStorageKeyLength)
        {
            throw new DomainException(
                "Image storage key cannot exceed " +
                $"{MaximumStorageKeyLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainException(
                "Image content type is required.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException(
                "Image size must be greater than zero.");
        }

        if (uploadedBy == UserId.Empty)
        {
            throw new DomainException(
                "Image uploader is required.");
        }

        if (uploadedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Image timestamps must be UTC.");
        }

        return new CommunityImage(
            CommunityImageId.New(),
            normalizedKey,
            contentType.Trim().ToLowerInvariant(),
            sizeBytes,
            uploadedBy,
            uploadedOnUtc);
    }
}
