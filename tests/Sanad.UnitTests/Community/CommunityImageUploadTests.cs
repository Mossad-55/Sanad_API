using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Community.Application.Uploads;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityImageUploadTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"community-image-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task Upload_ShouldPersistRecordWithoutCreatingPost()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        byte[] jpeg = [0xff, 0xd8, 0xff, 0x00, 0x01, 0x02];
        var result = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(jpeg),
                "image/jpeg",
                jpeg.Length),
            default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.ImageId);
        Assert.StartsWith("/files/community/", result.Value.ImageUrl);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.Equal(jpeg.Length, result.Value.SizeBytes);

        Assert.Equal(1, await db.CommunityImages.CountAsync());
        Assert.Equal(0, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task Upload_ShouldAcceptPngAndWebpSignatures()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0x00];
        var pngResult = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(png),
                "image/png",
                png.Length),
            default);

        Assert.True(pngResult.IsSuccess);

        byte[] webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P', 0x00];
        var webpResult = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(webp),
                "image/webp",
                webp.Length),
            default);

        Assert.True(webpResult.IsSuccess);
        Assert.Equal(2, await db.CommunityImages.CountAsync());
    }

    [Fact]
    public async Task Upload_ShouldRejectSignatureMismatch()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        byte[] executable = [(byte)'M', (byte)'Z', 0x00, 0x01];
        var result = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(executable),
                "image/jpeg",
                executable.Length),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityImageErrors.Invalid, result.Error);
        Assert.Equal(0, await db.CommunityImages.CountAsync());
    }

    [Fact]
    public async Task Upload_ShouldRejectUnsupportedType()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        byte[] text = [(byte)'h', (byte)'i'];
        var result = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(text),
                "text/plain",
                text.Length),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityImageErrors.Invalid, result.Error);
    }

    [Fact]
    public async Task Upload_ShouldRejectEmptyFile()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var result = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream([]),
                "image/jpeg",
                0),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(0, await db.CommunityImages.CountAsync());
    }

    [Fact]
    public async Task Upload_ShouldRejectOversizedFile()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        byte[] oversized = new byte[2_097_153];
        oversized[0] = 0xff;
        oversized[1] = 0xd8;
        oversized[2] = 0xff;

        var result = await handler.Handle(
            new UploadCommunityImageCommand(
                UserId.New(),
                new MemoryStream(oversized),
                "image/jpeg",
                oversized.Length),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(0, await db.CommunityImages.CountAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private UploadCommunityImageCommandHandler CreateHandler(
        CommunityDbContext db) =>
        new(
            db,
            new LocalDiskFileStorage(
                Options.Create(
                    new LocalStorageOptions { RootPath = _storageRoot })));

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
