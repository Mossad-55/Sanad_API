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

    [Fact]
    public async Task Delete_ShouldRemoveSavedFile()
    {
        using MemoryStream content =
            new([0x1, 0x2, 0x3, 0x4]);

        Result<StoredFile> saved =
            await _storage.SaveAsync(
                content,
                "image/jpeg",
                4,
                "splash",
                CancellationToken.None);

        Assert.True(saved.IsSuccess);

        string key =
            saved.Value.Key;

        string storedPath =
            Path.Combine(
                _rootPath,
                key.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        Assert.True(File.Exists(storedPath));

        Result delete =
            await _storage.DeleteAsync(
                key,
                CancellationToken.None);

        Assert.True(delete.IsSuccess);
        Assert.False(File.Exists(storedPath));
    }

    [Fact]
    public async Task Delete_ShouldRejectEmptyKey()
    {
        Result delete =
            await _storage.DeleteAsync(
                "",
                CancellationToken.None);

        Assert.True(delete.IsFailure);
        Assert.Equal(
            StorageErrors.Empty.Code,
            delete.Error.Code);
    }

    [Fact]
    public async Task SavePrivate_ShouldStorePdfUnderPrivateKey()
    {
        byte[] payload = [0x1, 0x2, 0x3, 0x4];

        using MemoryStream content = new(payload);

        Result<StoredFile> result =
            await _storage.SavePrivateAsync(
                content,
                "application/pdf",
                payload.Length,
                "caregiver-certificates",
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith(
            "private/caregiver-certificates/",
            result.Value.Key);
        Assert.EndsWith(".pdf", result.Value.Key);
    }

    [Fact]
    public async Task OpenRead_ShouldReturnSavedPrivateContent()
    {
        byte[] payload = [0x9, 0x8, 0x7];

        using MemoryStream content = new(payload);

        Result<StoredFile> saved =
            await _storage.SavePrivateAsync(
                content,
                "application/pdf",
                payload.Length,
                "caregiver-certificates",
                CancellationToken.None);

        Assert.True(saved.IsSuccess);

        Result<PrivateFileContent> read =
            await _storage.OpenReadAsync(
                saved.Value.Key,
                CancellationToken.None);

        Assert.True(read.IsSuccess);
        Assert.Equal(
            "application/pdf",
            read.Value.ContentType);

        await using Stream fileContent =
            read.Value.Content;

        using MemoryStream buffer = new();

        await fileContent.CopyToAsync(buffer);

        Assert.Equal(payload, buffer.ToArray());
    }

    [Fact]
    public async Task SavePrivate_ShouldRejectUnsupportedContentType()
    {
        byte[] payload = [0x1];

        using MemoryStream content = new(payload);

        Result<StoredFile> result =
            await _storage.SavePrivateAsync(
                content,
                "application/zip",
                payload.Length,
                "caregiver-certificates",
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.UnsupportedType, result.Error);
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