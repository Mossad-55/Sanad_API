using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalDiskFileStorage : IFileStorage
{
    private const long PrivateMaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private static readonly HashSet<string> PrivateAllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
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

        string fullPath = ResolveFullPath(key);

        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)!);

        await using FileStream fileStream =
            File.Create(fullPath);

        await content.CopyToAsync(
            fileStream,
            cancellationToken);

        return new StoredFile(key);
    }
    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            return Result.Failure(
                StorageErrors.Empty);
        }

        string normalizedKey =
            key.Trim()
                .Replace('\\', '/')
                .Trim('/');

        if (normalizedKey.Contains(
                "..",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(
                normalizedKey))
        {
            return Result.Failure(
                StorageErrors.UnsafePath);
        }


        string fullPath = ResolveFullPath(normalizedKey);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Result.Success();
    }

    public async Task<Result<StoredFile>> SavePrivateAsync(
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

        if (contentLength > PrivateMaxBytes)
        {
            return StorageErrors.TooLarge;
        }

        if (string.IsNullOrWhiteSpace(contentType) ||
            !PrivateAllowedContentTypes.Contains(contentType))
        {
            return StorageErrors.UnsupportedType;
        }

        string extension = contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        string safeFolder =
            string.IsNullOrWhiteSpace(folder)
                ? "documents"
                : folder.Trim().Replace('\\', '/').Trim('/');

        if (safeFolder.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(safeFolder))
        {
            safeFolder = "documents";
        }

        string key =
            $"private/{safeFolder}/{Guid.CreateVersion7():N}{extension}";

        string fullPath = ResolveFullPath(key);

        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)!);

        await using FileStream fileStream =
            File.Create(fullPath);

        await content.CopyToAsync(
            fileStream,
            cancellationToken);

        return new StoredFile(key);
    }

    public Task<Result<PrivateFileContent>> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult<Result<PrivateFileContent>>(
                Result<PrivateFileContent>.Failure(
                    StorageErrors.Empty));
        }

        string normalizedKey =
            NormalizeKey(key);

        if (!normalizedKey.StartsWith(
                "private/",
                StringComparison.Ordinal) ||
            normalizedKey.Contains("..", StringComparison.Ordinal))
        {
            return Task.FromResult<Result<PrivateFileContent>>(
                Result<PrivateFileContent>.Failure(
                    StorageErrors.UnsafePath));
        }

        string fullPath = ResolveFullPath(normalizedKey);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Result<PrivateFileContent>>(
                Result<PrivateFileContent>.Failure(
                    StorageErrors.NotFound));
        }

        string contentType =
            Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

        Stream content =
            File.OpenRead(fullPath);

        return Task.FromResult<Result<PrivateFileContent>>(
            new PrivateFileContent(
                normalizedKey,
                contentType,
                content));
    }

    private static string NormalizeKey(string key) =>
        key.Trim()
            .Replace('\\', '/')
            .Trim('/');

    private string ResolveFullPath(string normalizedKey)
    {
        string root = _options.GetEffectiveRootPath();

        if (normalizedKey.StartsWith(
                "private/",
                StringComparison.Ordinal))
        {
            // Sibling directory that UseStaticFiles never serves.
            root = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + "-private";
        }

        return Path.Combine(
            root,
            normalizedKey.Replace(
                '/',
                Path.DirectorySeparatorChar));
    }
}