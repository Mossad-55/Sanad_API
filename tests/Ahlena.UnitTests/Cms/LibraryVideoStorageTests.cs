using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Application.Library;
using Ahlena.Modules.Cms.Domain.Library;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Cms;

/// <summary>
/// Storage-backed library video contract. The uploaded bytes are the only
/// playback source (locked decision D6): the legacy external <c>videoUrl</c> is
/// administrator metadata that is validated when supplied but never exposed as
/// a consumer address, an external URL alone never makes a video playable, and
/// every read re-checks publication, content type and the premium entitlement.
/// </summary>
public sealed class LibraryVideoStorageTests
{
    private const string StoredKey = "library-video/approved.mp4";

    [Fact]
    public async Task Model_pins_the_private_storage_key_column_and_keeps_the_flag_unmapped()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType =
            db.Model.FindEntityType(typeof(LibraryContent))!;
        Microsoft.EntityFrameworkCore.Metadata.IProperty property =
            entityType.FindProperty(nameof(LibraryContent.VideoStorageKey))!;

        Assert.Equal("video_storage_key", property.GetColumnName());
        Assert.Equal((int?)LibraryContent.MaximumVideoStorageKeyLength, property.GetMaxLength());
        Assert.True(property.IsColumnNullable());
        // The projected flag is a computed property, never a stored column.
        Assert.Null(entityType.FindProperty(nameof(LibraryContent.HasStoredVideo)));
        Assert.Equal("video_url", entityType.FindProperty(nameof(LibraryContent.VideoUrl))!.GetColumnName());
    }

    [Fact]
    public async Task Video_content_is_creatable_without_an_external_url_and_starts_without_bytes()
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLibraryContentCommandHandler(db).Handle(
            VideoCommand() with { VideoUrl = null }, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Null(created.Value.VideoUrl);
        Assert.Null(created.Value.VideoStorageKey);
        LibraryContent stored = Assert.Single(await ReadAllAsync(db));
        Assert.False(stored.HasStoredVideo);
        Assert.Equal(300, stored.DurationSeconds);
    }

    [Theory]
    [InlineData("/watch?v=x")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.org/lesson.mp4")]
    public async Task Supplied_external_url_is_still_validated_when_present(string videoUrl)
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLibraryContentCommandHandler(db).Handle(
            VideoCommand() with { VideoUrl = videoUrl }, CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(LibraryErrors.ContentInvalid.Code, created.Error.Code);
        Assert.True(created.Error.Errors!.ContainsKey("videoUrl"));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_external_url_is_stored_as_absent_metadata(string? videoUrl)
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLibraryContentCommandHandler(db).Handle(
            VideoCommand() with { VideoUrl = videoUrl }, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Null(created.Value.VideoUrl);
    }

    [Fact]
    public void Domain_rejects_a_stored_reference_on_article_content()
    {
        DomainException exception = Assert.Throws<DomainException>(() => LibraryContent.Create(
            LibraryContentType.Article,
            null,
            "عنوان",
            "Title",
            "ملخص",
            "Summary",
            videoUrl: null,
            videoStorageKey: StoredKey,
            durationSeconds: null,
            coverImagePath: "library/cover.png",
            isPremium: false,
            sections: [Section(1)],
            DateTime.UtcNow));

        Assert.Equal("Article content cannot have a video file.", exception.Message);
    }

    [Theory]
    [InlineData("../private/secret.mp4")]
    [InlineData("/etc/passwd")]
    [InlineData("private\\library-video\\a.mp4")]
    [InlineData("C:/tmp/a.mp4")]
    public async Task AttachVideo_refuses_a_reference_outside_the_private_storage_area(string storageKey)
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent video = await CreateVideoAsync(db);

        DomainException exception =
            Assert.Throws<DomainException>(() => video.AttachVideo(storageKey));

        Assert.Equal("Stored video reference is invalid.", exception.Message);
        Assert.False(video.HasStoredVideo);
    }

    [Fact]
    public async Task AttachVideo_refuses_an_overlong_reference_and_a_blank_one()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent video = await CreateVideoAsync(db);

        Assert.Equal(
            "Stored video reference is invalid.",
            Assert.Throws<DomainException>(
                () => video.AttachVideo(new string('a', LibraryContent.MaximumVideoStorageKeyLength + 1))).Message);
        Assert.Equal(
            "A stored video reference is required.",
            Assert.Throws<DomainException>(() => video.AttachVideo("  ")).Message);
    }

    [Fact]
    public async Task AttachVideo_persists_the_reference_for_draft_and_published_content()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent draft = await CreateVideoAsync(db, publish: false);
        LibraryContent published = await CreateVideoAsync(db, publish: true);

        draft.AttachVideo(StoredKey);
        published.AttachVideo(StoredKey);
        await db.SaveChangesAsync();

        Assert.Equal("Draft", draft.Status.ToString());
        Assert.True(draft.HasStoredVideo);
        Assert.True(published.HasStoredVideo);
        foreach (LibraryContent reloaded in await ReadAllAsync(db))
            Assert.Equal(StoredKey, reloaded.VideoStorageKey);
    }

    [Fact]
    public async Task AttachVideo_refuses_archived_content_and_non_video_content()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.Archive();
        LibraryContent article = await CreateArticleAsync(db);

        Assert.Equal(
            "Archived library content cannot be modified.",
            Assert.Throws<DomainException>(() => archived.AttachVideo(StoredKey)).Message);
        Assert.Equal(
            "Only video content can have a video file.",
            Assert.Throws<DomainException>(() => article.AttachVideo(StoredKey)).Message);
        Assert.False(archived.HasStoredVideo);
        Assert.False(article.HasStoredVideo);
    }

    [Fact]
    public async Task RemoveVideo_clears_the_bytes_and_keeps_the_legacy_metadata()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent video = await CreateVideoAsync(db);
        video.AttachVideo(StoredKey);
        await db.SaveChangesAsync();

        video.RemoveVideo();
        await db.SaveChangesAsync();

        Assert.False(video.HasStoredVideo);
        Assert.Null(video.VideoStorageKey);
        // Removing the bytes is not a metadata edit: the admin metadata survives
        // so an administrator can still see what the source used to be.
        Assert.Equal("https://youtu.be/abc123", video.VideoUrl);
        Assert.Equal(300, video.DurationSeconds);
    }

    [Fact]
    public async Task RemoveVideo_refuses_archived_content_and_non_video_content()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.AttachVideo(StoredKey);
        archived.Archive();
        LibraryContent article = await CreateArticleAsync(db);

        Assert.Equal(
            "Archived library content cannot be modified.",
            Assert.Throws<DomainException>(() => archived.RemoveVideo()).Message);
        Assert.Equal(
            "Only video content can have a video file.",
            Assert.Throws<DomainException>(() => article.RemoveVideo()).Message);
        Assert.Equal(StoredKey, archived.VideoStorageKey);
    }

    [Fact]
    public async Task Metadata_update_keeps_already_uploaded_bytes()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent video = await CreateVideoAsync(db);
        video.AttachVideo(StoredKey);
        await db.SaveChangesAsync();
        Guid id = video.Id.Value;

        var updated = await new UpdateLibraryContentCommandHandler(db).Handle(
            new UpdateLibraryContentCommand(
                id,
                null,
                "عنوان محدث",
                "Updated title",
                "ملخص",
                "Summary",
                null,
                300,
                null,
                false,
                null),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(StoredKey, updated.Value.VideoStorageKey);
        LibraryContent reloaded = await ReadAsync(db, id);
        Assert.True(reloaded.HasStoredVideo);
        Assert.Null(reloaded.VideoUrl);
    }

    [Fact]
    public async Task Detail_exposes_the_relative_playback_route_only_with_bytes_and_entitlement()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();
        UserId subscriber = UserId.New();
        UserId stranger = UserId.New();
        SeedPremiumFamily(families, subscriber);

        LibraryContent premium = await CreateVideoAsync(db, publish: true, isPremium: true);
        Guid premiumId = premium.Id.Value;
        var handler = new GetLibraryContentQueryHandler(db, families);

        // Metadata only: an external URL is never a playback address.
        var withoutBytes = await handler.Handle(
            new GetLibraryContentQuery(premiumId, subscriber.Value), CancellationToken.None);
        Assert.True(withoutBytes.IsSuccess);
        Assert.True(withoutBytes.Value.CanAccess);
        Assert.Null(withoutBytes.Value.VideoUrl);

        premium.AttachVideo(StoredKey);
        await db.SaveChangesAsync();

        var entitled = await handler.Handle(
            new GetLibraryContentQuery(premiumId, subscriber.Value), CancellationToken.None);
        Assert.True(entitled.IsSuccess);
        Assert.True(entitled.Value.CanAccess);
        Assert.Equal($"/api/v1/library/content/{premiumId:D}/video", entitled.Value.VideoUrl);
        Assert.Equal(LibraryVideoUrls.For(premiumId), entitled.Value.VideoUrl);
        Assert.DoesNotContain(StoredKey, entitled.Value.VideoUrl);

        // Bytes exist but the caller is not entitled to premium content.
        var unentitled = await handler.Handle(
            new GetLibraryContentQuery(premiumId, stranger.Value), CancellationToken.None);
        Assert.True(unentitled.IsSuccess);
        Assert.False(unentitled.Value.CanAccess);
        Assert.Null(unentitled.Value.VideoUrl);
    }

    [Fact]
    public async Task Video_query_returns_the_stored_key_only_for_published_entitled_video()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();
        UserId subscriber = UserId.New();
        UserId stranger = UserId.New();
        SeedPremiumFamily(families, subscriber);

        LibraryContent free = await CreateVideoAsync(db, publish: true);
        free.AttachVideo(StoredKey);

        LibraryContent premium = await CreateVideoAsync(db, isPremium: true, publish: true);
        premium.AttachVideo("library-video/premium.mp4");

        LibraryContent draft = await CreateVideoAsync(db);
        draft.AttachVideo("library-video/draft.mp4");

        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.AttachVideo("library-video/archived.mp4");
        archived.Archive();

        LibraryContent withoutBytes = await CreateVideoAsync(db, publish: true);
        LibraryContent article = await CreateArticleAsync(db, publish: true);
        await db.SaveChangesAsync();

        var handler = new GetLibraryContentVideoQueryHandler(db, families);

        var freeRead = await handler.Handle(
            new GetLibraryContentVideoQuery(free.Id.Value, stranger.Value), CancellationToken.None);
        Assert.True(freeRead.IsSuccess);
        Assert.Equal(StoredKey, freeRead.Value.StorageKey);

        var premiumRead = await handler.Handle(
            new GetLibraryContentVideoQuery(premium.Id.Value, subscriber.Value), CancellationToken.None);
        Assert.True(premiumRead.IsSuccess);
        Assert.Equal("library-video/premium.mp4", premiumRead.Value.StorageKey);

        foreach (Guid failingId in new[]
        {
            premium.Id.Value,   // bytes exist, caller is not entitled
            draft.Id.Value,
            archived.Id.Value,
            withoutBytes.Id.Value,
            article.Id.Value,
            Guid.NewGuid()
        })
        {
            var failing = await handler.Handle(
                new GetLibraryContentVideoQuery(failingId, stranger.Value), CancellationToken.None);
            Assert.True(failing.IsFailure);
            Assert.Equal(LibraryErrors.ContentNotPublished.Code, failing.Error.Code);
        }
    }

    [Fact]
    public async Task Attach_and_remove_handlers_map_lifecycle_and_type_failures()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.Archive();
        LibraryContent article = await CreateArticleAsync(db);

        var attached = await new AttachLibraryContentVideoCommandHandler(db).Handle(
            new AttachLibraryContentVideoCommand(video.Id.Value, StoredKey), CancellationToken.None);
        Assert.True(attached.IsSuccess);
        Assert.Equal(StoredKey, attached.Value.VideoStorageKey);

        var unknown = await new AttachLibraryContentVideoCommandHandler(db).Handle(
            new AttachLibraryContentVideoCommand(Guid.NewGuid(), StoredKey), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentNotFound.Code, unknown.Error.Code);

        var archivedAttach = await new AttachLibraryContentVideoCommandHandler(db).Handle(
            new AttachLibraryContentVideoCommand(archived.Id.Value, StoredKey), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentInvalidOperation.Code, archivedAttach.Error.Code);

        var articleAttach = await new AttachLibraryContentVideoCommandHandler(db).Handle(
            new AttachLibraryContentVideoCommand(article.Id.Value, StoredKey), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentInvalid.Code, articleAttach.Error.Code);
        Assert.True(articleAttach.Error.Errors!.ContainsKey("videoStorageKey"));

        var removed = await new RemoveLibraryContentVideoCommandHandler(db).Handle(
            new RemoveLibraryContentVideoCommand(video.Id.Value), CancellationToken.None);
        Assert.True(removed.IsSuccess);
        Assert.Null(removed.Value.VideoStorageKey);

        var archivedRemove = await new RemoveLibraryContentVideoCommandHandler(db).Handle(
            new RemoveLibraryContentVideoCommand(archived.Id.Value), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentInvalidOperation.Code, archivedRemove.Error.Code);

        var articleRemove = await new RemoveLibraryContentVideoCommandHandler(db).Handle(
            new RemoveLibraryContentVideoCommand(article.Id.Value), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentInvalid.Code, articleRemove.Error.Code);
        Assert.True(articleRemove.Error.Errors!.ContainsKey("videoStorageKey"));

        var unknownRemove = await new RemoveLibraryContentVideoCommandHandler(db).Handle(
            new RemoveLibraryContentVideoCommand(Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(LibraryErrors.ContentNotFound.Code, unknownRemove.Error.Code);
    }

    // ---- helpers ---------------------------------------------------------------

    private static CreateLibraryContentCommand VideoCommand() => new(
        LibraryContentType.Video,
        null,
        "فيديو",
        "Video",
        "ملخص",
        "Summary",
        "https://youtu.be/abc123",
        300,
        "library/cover.png",
        false,
        null);

    private static CreateLibraryContentCommand ArticleCommand() => new(
        LibraryContentType.Article,
        null,
        "عنوان",
        "Title",
        "ملخص",
        "Summary",
        null,
        null,
        "library/cover.png",
        false,
        [new LibraryContentSectionInput(1, "الأول", "First", ["نقطة"], ["bullet"])]);

    private static LibraryContentSectionDraft Section(int order) =>
        new(order, "الأول", "First", ["نقطة"], ["bullet"]);

    private static async Task<LibraryContent> CreateVideoAsync(
        CmsDbContext db, bool publish = false, bool isPremium = false)
    {
        LibraryContent content = await CreateAsync(db, VideoCommand() with { IsPremium = isPremium });
        if (publish)
        {
            content.Publish();
            await db.SaveChangesAsync();
        }

        return content;
    }

    private static async Task<LibraryContent> CreateArticleAsync(CmsDbContext db, bool publish = false)
    {
        LibraryContent content = await CreateAsync(db, ArticleCommand());
        if (publish)
        {
            content.Publish();
            await db.SaveChangesAsync();
        }

        return content;
    }

    private static async Task<LibraryContent> CreateAsync(CmsDbContext db, CreateLibraryContentCommand command)
    {
        var result = await new CreateLibraryContentCommandHandler(db)
            .Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return await ReadAsync(db, result.Value.Id);
    }

    private static void SeedPremiumFamily(FamiliesDbContext families, UserId owner)
    {
        var family = Family.Create(owner);
        var version = SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus);
        families.Families.Add(family);
        families.SubscriptionPlanVersions.Add(version);
        families.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, version));
        families.SaveChanges();
    }

    private static async Task<List<LibraryContent>> ReadAllAsync(CmsDbContext db) =>
        await db.LibraryContents.AsNoTracking().ToListAsync();

    // Tracked on purpose: the lifecycle tests mutate the returned aggregate.
    private static async Task<LibraryContent> ReadAsync(CmsDbContext db, Guid id) =>
        await db.LibraryContents.SingleAsync(content => content.Id.Value == id);

    private static CmsDbContext CreateCmsContext()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }
}
