using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Infrastructure.Storage;

namespace Sanad.UnitTests.BuildingBlocks;

public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _rootPath;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageTests()
    {
        _rootPath =
            Path.Combine(
                Path.GetTempPath(),
                "sanad-storage-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_rootPath);

        _storage =
            new LocalDiskFileStorage(
                Options.Create(
                    new LocalStorageOptions
                    {
                        RootPath = _rootPath
                    }));
    }

    [Fact]
    public async Task Save_ShouldStoreJpegUnderSplashKey()
    {
        byte[] payload =
            [0x1, 0x2, 0x3, 0x4];

        using MemoryStream content =
            new(payload);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/jpeg",
                payload.Length,
                "splash",
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        string key =
            result.Value.Key;

        Assert.StartsWith(
            "splash/",
            key);
        Assert.EndsWith(
            ".jpg",
            key);
        Assert.DoesNotContain(
            "..",
            key,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            '\\',
            key);

        string storedPath =
            Path.Combine(
                _rootPath,
                key.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        Assert.True(File.Exists(storedPath));
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(storedPath));
    }

    [Fact]
    public async Task Save_ShouldRejectEmptyContent()
    {
        using MemoryStream content =
            new();

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/jpeg",
                0,
                "splash",
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            StorageErrors.Empty.Code,
            result.Error.Code);
        Assert.Empty(
            Directory.GetFiles(
                _rootPath,
                "*",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_ShouldRejectOversizedContent()
    {
        using MemoryStream content =
            new([0x1]);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/jpeg",
                2_097_153,
                "splash",
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            StorageErrors.TooLarge.Code,
            result.Error.Code);
        Assert.Empty(
            Directory.GetFiles(
                _rootPath,
                "*",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_ShouldRejectUnsupportedContentType()
    {
        using MemoryStream content =
            new([0x1, 0x2, 0x3, 0x4]);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/gif",
                4,
                "splash",
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            StorageErrors.UnsupportedType.Code,
            result.Error.Code);
        Assert.Empty(
            Directory.GetFiles(
                _rootPath,
                "*",
                SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(
                _rootPath,
                true);
        }
    }
}