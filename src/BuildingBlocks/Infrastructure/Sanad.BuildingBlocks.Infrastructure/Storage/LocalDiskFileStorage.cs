using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalDiskFileStorage : IFileStorage
{
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private readonly LocalStorageOptions _options;

    public LocalDiskFileStorage(
        IOptions<LocalStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<Result<StoredFile>> SaveAsync(
        Stream content,
        string contentType,
        long contentLength,
        string folder,
        CancellationToken cancellationToken = default)
    {
        if (contentLength <= 0)
        {
            return StorageErrors.Empty;
        }

        if (contentLength > _options.MaxBytes)
        {
            return StorageErrors.TooLarge;
        }

        if (string.IsNullOrWhiteSpace(contentType) ||
            !AllowedContentTypes.Contains(contentType))
        {
            return StorageErrors.UnsupportedType;
        }

        string extension = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        string safeFolder =
            string.IsNullOrWhiteSpace(folder)
                ? "uploads"
                : folder.Trim().Replace('\\', '/').Trim('/');

        if (safeFolder.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(safeFolder))
        {
            safeFolder = "uploads";
        }

        string key =
            $"{safeFolder}/{Guid.CreateVersion7():N}{extension}";

        string root = _options.GetEffectiveRootPath();
        string fullPath = Path.Combine(
            root,
            key.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)!);

        await using FileStream fileStream =
            File.Create(fullPath);

        await content.CopyToAsync(
            fileStream,
            cancellationToken);

        return new StoredFile(key);
    }
}