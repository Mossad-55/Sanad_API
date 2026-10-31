using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Infrastructure.Storage;

namespace Ahlena.UnitTests.BuildingBlocks;

public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _rootPath;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageTests()
    {
        _rootPath =
            Path.Combine(
                Path.GetTempPath(),
                "ahlena-storage-tests",
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
    public async Task SavePrivate_ShouldRejectSvg()
    {
        // SVG is a public landing-media format only: private storage must never
        // accept it, whatever folder is named.
        byte[] payload =
            Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");

        using MemoryStream content =
            new(payload);

        Result<StoredFile> result =
            await _storage.SavePrivateAsync(
                content,
                "image/svg+xml",
                payload.Length,
                "caregiver-certificates",
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
    public async Task SavePrivate_MP4_ShouldRemainReadableFromANewStorageInstance()
    {
        // Synthetic MP4 header (ftyp box). Durability is proven by reopening the
        // bytes through a second storage instance built over the same root: no
        // in-memory state and no shared stream survives between the two.
        byte[] payload =
        [
            0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70,
            0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x02, 0x00,
            0x69, 0x73, 0x6F, 0x6D, 0x69, 0x73, 0x6F, 0x32,
            0x61, 0x76, 0x63, 0x31, 0x6D, 0x70, 0x34, 0x31
        ];

        using MemoryStream content = new(payload);

        Result<StoredFile> saved =
            await _storage.SavePrivateAsync(
                content,
                "video/mp4",
                payload.Length,
                "library-video",
                CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.StartsWith("private/library-video/", saved.Value.Key);
        Assert.EndsWith(".mp4", saved.Value.Key);

        string storedPath =
            Path.Combine(
                _rootPath + "-private",
                saved.Value.Key.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(storedPath));
        Assert.False(File.Exists(
            Path.Combine(
                _rootPath,
                saved.Value.Key.Replace('/', Path.DirectorySeparatorChar))));

        var reopened =
            new LocalDiskFileStorage(
                Options.Create(
                    new LocalStorageOptions
                    {
                        RootPath = _rootPath
                    }));

        Result<PrivateFileContent> read =
            await reopened.OpenReadAsync(
                saved.Value.Key,
                CancellationToken.None);

        Assert.True(read.IsSuccess);
        Assert.Equal("video/mp4", read.Value.ContentType);

        await using Stream stream = read.Value.Content;
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer);

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

    [Fact]
    public async Task SavePrivate_ShouldDeletePartialFileWhenActualStreamExceedsDeclaredMaximum()
    {
        byte[] payload = new byte[40];
        using MemoryStream content = new(payload);

        Result<StoredFile> result = await _storage.SavePrivateAsync(
            content,
            "application/pdf",
            contentLength: 16,
            folder: "care-home-documents",
            maximumBytes: 32,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.TooLarge.Code, result.Error.Code);
        Assert.Empty(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_ShouldStoreSvgUnderLandingKey()
    {
        byte[] payload =
            Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");

        using MemoryStream content = new(payload);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/svg+xml",
                payload.Length,
                "landing",
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("landing/", result.Value.Key);
        Assert.EndsWith(".svg", result.Value.Key);

        string storedPath =
            Path.Combine(
                _rootPath,
                result.Value.Key.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(payload, await File.ReadAllBytesAsync(storedPath));
    }

    // Cross-path guard: the only public writer allowed to persist image/svg+xml
    // is the landing media path (its bytes are sanitized first). Every other
    // uploader keeps the shared raster allowlist behaviour and is rejected here,
    // so no other path can store-and-serve an unsanitized SVG. Folders below are
    // the real folders each controller passes to SaveAsync.
    [Theory]
    [InlineData("splash")]            // AdminSplashScreensController.SaveSplashImageAsync
    [InlineData("assessment-tiers")]  // AdminAssessmentsController.SaveTierImageAsync
    [InlineData("services")]          // AdminLookupsController.SaveIconAsync
    [InlineData("library")]           // AdminLibraryController.SaveCoverAsync
    [InlineData("wellness-tips")]     // AdminWellnessTipsController.SaveImageAsync
    [InlineData("uploads")]           // default folder (no caller folder supplied)
    public async Task Save_ShouldRejectSvgForEveryNonLandingFolder(string folder)
    {
        byte[] payload =
            Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert('x')</script><rect/></svg>");

        using MemoryStream content = new(payload);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                "image/svg+xml",
                payload.Length,
                folder,
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrors.UnsupportedType.Code, result.Error.Code);
        Assert.Empty(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    public async Task Save_ShouldKeepRasterTypesWorking(string contentType, string extension)
    {
        byte[] payload = [0x1, 0x2, 0x3, 0x4];

        using MemoryStream content = new(payload);

        Result<StoredFile> result =
            await _storage.SaveAsync(
                content,
                contentType,
                payload.Length,
                "assessment-tiers",
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("assessment-tiers/", result.Value.Key);
        Assert.EndsWith(extension, result.Value.Key);
    }

    [Fact]
    public void Serving_ShouldHardenSvgResponsesOnly()
    {
        var svgHeaders = new HeaderDictionary();

        Ahlena.API.ApplicationBuilderExtensions.ApplyStaticResponseHardening(
            "landing/cover.svg",
            svgHeaders);

        Assert.Equal(
            "nosniff",
            svgHeaders["X-Content-Type-Options"].ToString());
        Assert.Equal(
            Ahlena.API.ApplicationBuilderExtensions.SvgContentSecurityPolicy,
            svgHeaders["Content-Security-Policy"].ToString());
        Assert.Contains(
            "default-src 'none'",
            svgHeaders["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "sandbox",
            svgHeaders["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);

        // Raster media keeps its current behaviour: no added headers.
        var rasterHeaders = new HeaderDictionary();

        Ahlena.API.ApplicationBuilderExtensions.ApplyStaticResponseHardening(
            "landing/cover.png",
            rasterHeaders);

        Assert.True(string.IsNullOrEmpty(rasterHeaders["X-Content-Type-Options"].ToString()));
        Assert.True(string.IsNullOrEmpty(rasterHeaders["Content-Security-Policy"].ToString()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(
                _rootPath,
                true);
        }

        string privateRootPath = _rootPath + "-private";

        if (Directory.Exists(privateRootPath))
        {
            Directory.Delete(
                privateRootPath,
                true);
        }
    }
}
