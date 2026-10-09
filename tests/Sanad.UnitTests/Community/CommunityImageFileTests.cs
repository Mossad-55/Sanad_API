using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Uploads;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

public sealed class CommunityImageFileTests
{
    [Fact]
    public async Task Read_ShouldAllowModeratorPreviewOfPendingImage()
    {
        await using var db = CreateDb();
        var uploader = UserId.New();
        var record = await AddImageAsync(db, uploader);

        var result = await ReadAsync(db, record.Id.Value, UserId.New(), isModerator: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.Value.ContentType);
    }

    [Fact]
    public async Task Read_ShouldAllowUploaderOwnOrphanImage()
    {
        await using var db = CreateDb();
        var uploader = UserId.New();
        var record = await AddImageAsync(db, uploader);

        var result = await ReadAsync(db, record.Id.Value, uploader, isModerator: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Read_ShouldDenyStrangerBeforePublication()
    {
        await using var db = CreateDb();
        var record = await AddImageAsync(db, UserId.New());

        var result = await ReadAsync(db, record.Id.Value, UserId.New(), isModerator: false);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityImageFileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Read_ShouldAllowStrangerAfterPublication()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var record = await AddImageAsync(db, author);
        Post post = Post.Create(
            "Title Arabic", "Title", "Content Arabic", "Content",
            CommunityImageUrls.For(record.Id), false, author, Utc(1));
        post.Publish(UserId.New(), Utc(2));
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await ReadAsync(db, record.Id.Value, UserId.New(), isModerator: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Read_ShouldDenyStrangerWhenPostRejected()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var record = await AddImageAsync(db, author);
        Post post = Post.Create(
            "Title Arabic", "Title", "Content Arabic", "Content",
            CommunityImageUrls.For(record.Id), false, author, Utc(1));
        post.Reject(Utc(2));
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await ReadAsync(db, record.Id.Value, UserId.New(), isModerator: false);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityImageFileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Read_ShouldReturnNotFoundForUnknownImage()
    {
        await using var db = CreateDb();

        var result = await ReadAsync(db, Guid.CreateVersion7(), UserId.New(), isModerator: true);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityImageFileErrors.NotFound, result.Error);
    }

    private static async Task<Result<CommunityImageFileResponse>> ReadAsync(
        CommunityDbContext db, Guid imageId, UserId actor, bool isModerator) =>
        await new GetCommunityImageFileQueryHandler(db, new OpenStorage()).Handle(
            new GetCommunityImageFileQuery(imageId, actor, isModerator), default);

    private static async Task<Sanad.Modules.Community.Domain.Uploads.CommunityImage> AddImageAsync(
        CommunityDbContext db, UserId uploader)
    {
        var record = Sanad.Modules.Community.Domain.Uploads.CommunityImage.Create(
            "private/community/test.jpg", "image/jpeg", 4, uploader, Utc(1));
        db.CommunityImages.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class OpenStorage : IFileStorage
    {
        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoredFile>.Success(new StoredFile("private/community/test.jpg")));

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoredFile>.Success(new StoredFile("private/community/test.jpg")));

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PrivateFileContent>.Success(
                new PrivateFileContent(key, "image/jpeg", new MemoryStream([0xff, 0xd8, 0xff]))));

        public Task<Result> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
