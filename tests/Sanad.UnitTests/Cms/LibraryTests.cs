using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class LibraryTests
{
    [Fact]
    public void Model_ShouldMapLibraryTables()
    {
        using CmsDbContext db = CreateCmsContext();

        string[] expectedTables =
        [
            "library_categories",
            "library_contents",
            "library_content_interactions"
        ];

        foreach (string table in expectedTables)
        {
            Assert.Contains(
                db.Model.GetEntityTypes(),
                entityType => entityType.GetTableName() == table);
        }
    }

    [Fact]
    public async Task Category_crud_public_list_active_only_and_in_use_delete_guard()
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLibraryCategoryCommandHandler(db)
            .Handle(new CreateLibraryCategoryCommand("الصحة", "Health"), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.True(created.Value.IsActive);
        Assert.Null(created.Value.UpdatedOnUtc);

        var invalid = await new CreateLibraryCategoryCommandHandler(db)
            .Handle(new CreateLibraryCategoryCommand(" ", "Nope"), CancellationToken.None);
        Assert.True(invalid.IsFailure);
        Assert.Equal(LibraryErrors.CategoryInvalid.Code, invalid.Error.Code);

        var updated = await new UpdateLibraryCategoryCommandHandler(db)
            .Handle(new UpdateLibraryCategoryCommand(
                created.Value.Id, "العناية", "Care", IsActive: false), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Care", updated.Value.EnglishName);
        Assert.False(updated.Value.IsActive);
        Assert.NotNull(updated.Value.UpdatedOnUtc);

        var missing = await new UpdateLibraryCategoryCommandHandler(db)
            .Handle(new UpdateLibraryCategoryCommand(Guid.NewGuid(), "x", "x", true), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal(LibraryErrors.CategoryNotFound.Code, missing.Error.Code);

        LibraryCategory second = LibraryCategory.Create("رياضة", "Sports", true, DateTime.UtcNow);
        db.LibraryCategories.Add(second);
        await db.SaveChangesAsync();

        var publicList = await new GetLibraryCategoriesQueryHandler(db)
            .Handle(new GetLibraryCategoriesQuery(), CancellationToken.None);
        Assert.True(publicList.IsSuccess);
        Assert.Single(publicList.Value); // the deactivated category is hidden

        var contentResult = await new CreateLibraryContentCommandHandler(db)
            .Handle(ArticleCommand(second.Id.Value), CancellationToken.None);
        Assert.True(contentResult.IsSuccess);

        var inUse = await new DeleteLibraryCategoryCommandHandler(db)
            .Handle(new DeleteLibraryCategoryCommand(second.Id.Value), CancellationToken.None);
        Assert.True(inUse.IsFailure);
        Assert.Equal(LibraryErrors.CategoryInUse.Code, inUse.Error.Code);

        var unused = await new DeleteLibraryCategoryCommandHandler(db)
            .Handle(new DeleteLibraryCategoryCommand(created.Value.Id), CancellationToken.None);
        Assert.True(unused.IsSuccess);
    }

    [Fact]
    public async Task Create_content_validates_category_unknown_and_inactive()
    {
        await using CmsDbContext db = CreateCmsContext();

        var unknown = await new CreateLibraryContentCommandHandler(db)
            .Handle(ArticleCommand(Guid.NewGuid()), CancellationToken.None);
        Assert.True(unknown.IsFailure);
        Assert.Equal(LibraryErrors.InvalidCategory.Code, unknown.Error.Code);

        LibraryCategory inactive = LibraryCategory.Create("قديم", "Old", false, DateTime.UtcNow);
        db.LibraryCategories.Add(inactive);
        await db.SaveChangesAsync();

        var categoryInactive = await new CreateLibraryContentCommandHandler(db)
            .Handle(ArticleCommand(inactive.Id.Value), CancellationToken.None);
        Assert.True(categoryInactive.IsFailure);
        Assert.Equal(LibraryErrors.CategoryInactive.Code, categoryInactive.Error.Code);

        Assert.Empty(db.LibraryContents);
    }

    [Fact]
    public async Task Create_content_enforces_type_specific_rules()
    {
        await using CmsDbContext db = CreateCmsContext();
        var handler = new CreateLibraryContentCommandHandler(db);

        var duplicateOrders = await handler.Handle(
            ArticleCommand(null) with
            {
                Sections =
                [
                    new(1, "أ", "A", ["b1"], ["b1"]),
                    new(1, "ب", "B", ["b2"], ["b2"])
                ]
            },
            CancellationToken.None);
        Assert.True(duplicateOrders.IsFailure);
        Assert.Equal(LibraryErrors.ContentInvalid.Code, duplicateOrders.Error.Code);

        var articleWithVideo = await handler.Handle(
            ArticleCommand(null) with { VideoUrl = "https://youtu.be/x" }, CancellationToken.None);
        Assert.True(articleWithVideo.IsFailure);

        var videoWithoutDuration = await handler.Handle(
            VideoCommand() with { DurationSeconds = null }, CancellationToken.None);
        Assert.True(videoWithoutDuration.IsFailure);

        var videoWithSections = await handler.Handle(
            VideoCommand() with { Sections = [new(1, "أ", "A", ["b"], ["b"])] },
            CancellationToken.None);
        Assert.True(videoWithSections.IsFailure);

        var videoWithRelativeUrl = await handler.Handle(
            VideoCommand() with { VideoUrl = "/watch?v=x" }, CancellationToken.None);
        Assert.True(videoWithRelativeUrl.IsFailure);

        Assert.Empty(db.LibraryContents);

        var article = await handler.Handle(ArticleCommand(null), CancellationToken.None);
        Assert.True(article.IsSuccess);
        Assert.Equal("Draft", article.Value.Status);
        Assert.Equal(2, article.Value.Sections.Count);
        Assert.Equal([1, 2], article.Value.Sections.Select(section => section.DisplayOrder));

        var video = await handler.Handle(VideoCommand(), CancellationToken.None);
        Assert.True(video.IsSuccess);
        Assert.Equal("https://youtu.be/abc123", video.Value.VideoUrl);
        Assert.Equal(300, video.Value.DurationSeconds);
    }

    [Fact]
    public async Task Draft_lifecycle_update_publish_archive_rules()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent published = await CreatePublished(db, "Article", "Published");

        var updatePublished = await new UpdateLibraryContentCommandHandler(db)
            .Handle(new UpdateLibraryContentCommand(
                published.Id.Value, null, "ت", "T", "س", "S", null, null, null, false, null),
            CancellationToken.None);
        Assert.True(updatePublished.IsFailure);
        Assert.Equal(LibraryErrors.ContentInvalidOperation.Code, updatePublished.Error.Code);

        LibraryContent draft = await CreateDraft(db, "Article", "Draft");
        var updatedDraft = await new UpdateLibraryContentCommandHandler(db)
            .Handle(new UpdateLibraryContentCommand(
                draft.Id.Value, null, "محدث", "Updated", "س", "S", null, null, null, true,
                [new(1, "وحده", "Solo", ["ب"], ["b"])]),
            CancellationToken.None);
        Assert.True(updatedDraft.IsSuccess);
        Assert.Equal("Updated", updatedDraft.Value.EnglishTitle);
        Assert.True(updatedDraft.Value.IsPremium);
        Assert.Single(updatedDraft.Value.Sections);

        var publish = await new PublishLibraryContentCommandHandler(db)
            .Handle(new PublishLibraryContentCommand(draft.Id.Value), CancellationToken.None);
        Assert.True(publish.IsSuccess);
        Assert.Equal("Published", publish.Value.Status);
        Assert.NotNull(publish.Value.PublishedOnUtc);

        var archive = await new ArchiveLibraryContentCommandHandler(db)
            .Handle(new ArchiveLibraryContentCommand(draft.Id.Value), CancellationToken.None);
        Assert.True(archive.IsSuccess);
        Assert.Equal("Archived", archive.Value.Status);

        var republish = await new PublishLibraryContentCommandHandler(db)
            .Handle(new PublishLibraryContentCommand(draft.Id.Value), CancellationToken.None);
        Assert.True(republish.IsFailure);
        Assert.Equal(LibraryErrors.ContentInvalidOperation.Code, republish.Error.Code);
    }

    [Fact]
    public async Task Public_list_filters_searches_and_sorts_published_only()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        LibraryCategory health = LibraryCategory.Create("صحة", "Health", true, DateTime.UtcNow);
        LibraryCategory sports = LibraryCategory.Create("رياضة", "Sports", true, DateTime.UtcNow);
        db.LibraryCategories.AddRange(health, sports);
        await db.SaveChangesAsync();

        LibraryContent a = await CreatePublished(db, "Article", "Healthy sleep", health.Id.Value);
        LibraryContent b = await CreatePublished(db, "Video", "Sports day", health.Id.Value);
        b.IncrementViews();
        b.IncrementViews();
        LibraryContent c = await CreatePublished(db, "Article", "نصيحة صحية", sports.Id.Value);
        await CreateDraft(db, "Article", "Draft item");
        await db.SaveChangesAsync();

        Guid aId = a.Id.Value;
        Guid bId = b.Id.Value;
        Guid cId = c.Id.Value;

        var handler = new GetLibraryContentListQueryHandler(db, families);

        var all = await handler.Handle(new GetLibraryContentListQuery(), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(3, all.Value.Count);
        Assert.DoesNotContain(all.Value, item => item.EnglishTitle == "Draft item");

        var byType = await handler.Handle(
            new GetLibraryContentListQuery(LibraryContentType.Video), CancellationToken.None);
        Assert.Equal(bId, Assert.Single(byType.Value).Id);

        var byCategory = await handler.Handle(
            new GetLibraryContentListQuery(CategoryId: sports.Id.Value), CancellationToken.None);
        Assert.Equal(cId, Assert.Single(byCategory.Value).Id);

        var searchArabic = await handler.Handle(
            new GetLibraryContentListQuery(Search: "صحية"), CancellationToken.None);
        Assert.Equal(cId, Assert.Single(searchArabic.Value).Id);

        var searchEnglish = await handler.Handle(
            new GetLibraryContentListQuery(Search: "SLEEP"), CancellationToken.None);
        Assert.Equal(aId, Assert.Single(searchEnglish.Value).Id);

        var newest = await handler.Handle(
            new GetLibraryContentListQuery(Sort: LibraryContentSort.Newest), CancellationToken.None);
        Assert.Equal([cId, bId, aId], newest.Value.Select(item => item.Id));

        var mostViewed = await handler.Handle(
            new GetLibraryContentListQuery(Sort: LibraryContentSort.MostViewed), CancellationToken.None);
        Assert.Equal(bId, mostViewed.Value[0].Id);

        Assert.All(all.Value, item =>
        {
            Assert.False(item.IsLiked);
            Assert.False(item.IsSaved);
            Assert.False(item.IsPremium);
            Assert.True(item.CanAccess);
        });
    }

    [Fact]
    public async Task Interactions_saved_liked_views_are_idempotent_and_published_only()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();
        LibraryContent published = await CreatePublished(db, "Article", "Published");
        LibraryContent draft = await CreateDraft(db, "Article", "Draft");
        await db.SaveChangesAsync();
        UserId user = UserId.New();
        Guid publishedId = published.Id.Value;
        Guid draftId = draft.Id.Value;

        var savedDraft = await SetSaved(db, draftId, user, true);
        Assert.True(savedDraft.IsFailure);
        Assert.Equal(LibraryErrors.ContentNotFound.Code, savedDraft.Error.Code);

        Assert.True((await SetSaved(db, publishedId, user, true)).IsSuccess);
        Assert.True((await SetSaved(db, publishedId, user, true)).IsSuccess);
        Assert.Equal(1, db.LibraryContentInteractions.Count());

        Assert.True((await SetLiked(db, publishedId, user, true)).IsSuccess);
        Assert.True((await SetLiked(db, publishedId, user, true)).IsSuccess);
        Assert.Equal(1, published.LikesCount);

        Assert.True((await SetLiked(db, publishedId, user, false)).IsSuccess);
        Assert.True((await SetLiked(db, publishedId, user, false)).IsSuccess);
        Assert.Equal(0, published.LikesCount);
        Assert.Single(db.LibraryContentInteractions);

        var viewDraft = await new RecordLibraryContentViewCommandHandler(db)
            .Handle(new RecordLibraryContentViewCommand(draftId), CancellationToken.None);
        Assert.True(viewDraft.IsFailure);

        var views = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var view = await new RecordLibraryContentViewCommandHandler(db)
                .Handle(new RecordLibraryContentViewCommand(publishedId), CancellationToken.None);
            Assert.True(view.IsSuccess);
            views.Add(view.Value);
        }

        Assert.Equal([1, 2, 3], views);

        var detail = await new GetLibraryContentQueryHandler(db, families)
            .Handle(new GetLibraryContentQuery(publishedId, user.Value), CancellationToken.None);
        Assert.True(detail.IsSuccess);
        Assert.True(detail.Value.IsSaved);
        Assert.False(detail.Value.IsLiked);
        Assert.Equal(3, detail.Value.ViewCount);
    }

    [Fact]
    public async Task Saved_list_returns_newest_saved_first_and_published_only()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();
        LibraryContent first = await CreatePublished(db, "Article", "First");
        LibraryContent second = await CreatePublished(db, "Article", "Second");
        LibraryContent third = await CreatePublished(db, "Video", "Third");
        LibraryContent draft = await CreateDraft(db, "Article", "Draft");
        await db.SaveChangesAsync();
        UserId user = UserId.New();

        await SetSaved(db, first.Id.Value, user, true);
        await SetSaved(db, second.Id.Value, user, true);
        await SetSaved(db, third.Id.Value, user, true);
        await SetSaved(db, second.Id.Value, user, false);
        await SetSaved(db, draft.Id.Value, user, true);

        var saved = await new GetSavedLibraryContentQueryHandler(db, families)
            .Handle(new GetSavedLibraryContentQuery(user.Value), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            [third.Id.Value, first.Id.Value],
            saved.Value.Select(item => item.Id));
    }

    [Fact]
    public async Task Detail_returns_not_published_for_drafts_and_archived()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();
        LibraryContent draft = await CreateDraft(db, "Article", "Draft");
        await db.SaveChangesAsync();
        Guid draftId = draft.Id.Value;

        var handler = new GetLibraryContentQueryHandler(db, families);

        var missing = await handler.Handle(
            new GetLibraryContentQuery(Guid.NewGuid(), Guid.Empty), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal(LibraryErrors.ContentNotPublished.Code, missing.Error.Code);

        var draftResult = await handler.Handle(
            new GetLibraryContentQuery(draftId, Guid.Empty), CancellationToken.None);
        Assert.True(draftResult.IsFailure);
        Assert.Equal(LibraryErrors.ContentNotPublished.Code, draftResult.Error.Code);

        await new PublishLibraryContentCommandHandler(db)
            .Handle(new PublishLibraryContentCommand(draftId), CancellationToken.None);
        await new ArchiveLibraryContentCommandHandler(db)
            .Handle(new ArchiveLibraryContentCommand(draftId), CancellationToken.None);

        var archived = await handler.Handle(
            new GetLibraryContentQuery(draftId, Guid.Empty), CancellationToken.None);
        Assert.True(archived.IsFailure);
        Assert.Equal(LibraryErrors.ContentNotPublished.Code, archived.Error.Code);
    }

    [Fact]
    public async Task Premium_detail_is_metadata_only_without_subscription_and_full_with_premium_plan()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        UserId subscriber = UserId.New();
        var family = Family.Create(subscriber);
        var version = SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus);
        families.Families.Add(family);
        families.SubscriptionPlanVersions.Add(version);
        families.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, version));
        await families.SaveChangesAsync();

        LibraryContent premium = await CreatePublished(db, "Video", "Premium video", isPremium: true);
        LibraryContent free = await CreatePublished(db, "Article", "Free article");
        Guid premiumId = premium.Id.Value;
        string videoUrl = premium.VideoUrl!;
        Guid freeId = free.Id.Value;

        var handler = new GetLibraryContentQueryHandler(db, families);

        var anonymous = await handler.Handle(
            new GetLibraryContentQuery(premiumId, UserId.New().Value), CancellationToken.None);
        Assert.True(anonymous.IsSuccess);
        Assert.True(anonymous.Value.IsPremium);
        Assert.False(anonymous.Value.CanAccess);
        Assert.Null(anonymous.Value.VideoUrl);
        Assert.Empty(anonymous.Value.Sections);

        var entitled = await handler.Handle(
            new GetLibraryContentQuery(premiumId, subscriber.Value), CancellationToken.None);
        Assert.True(entitled.IsSuccess);
        Assert.True(entitled.Value.CanAccess);
        Assert.Equal(videoUrl, entitled.Value.VideoUrl);

        var freeDetail = await handler.Handle(
            new GetLibraryContentQuery(freeId, UserId.New().Value), CancellationToken.None);
        Assert.True(freeDetail.IsSuccess);
        Assert.True(freeDetail.Value.CanAccess);
        Assert.NotEmpty(freeDetail.Value.Sections);
    }

    [Fact]
    public async Task Recommendations_same_category_newest_then_top_viewed_fallback_exclude_seed()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        LibraryCategory health = LibraryCategory.Create("صحة", "Health", true, DateTime.UtcNow);
        db.LibraryCategories.Add(health);
        await db.SaveChangesAsync();

        LibraryContent seed = await CreatePublished(db, "Article", "Seed", health.Id.Value);
        LibraryContent sameNewer = await CreatePublished(db, "Video", "Same newer", health.Id.Value);
        LibraryContent other = await CreatePublished(db, "Article", "Other");
        other.IncrementViews();
        other.IncrementViews();
        await db.SaveChangesAsync();
        Guid seedId = seed.Id.Value;
        Guid sameNewerId = sameNewer.Id.Value;
        Guid otherId = other.Id.Value;

        var handler = new GetLibraryRecommendationsQueryHandler(db, families);

        var fromSeed = await handler.Handle(
            new GetLibraryRecommendationsQuery(ContentId: seedId), CancellationToken.None);
        Assert.True(fromSeed.IsSuccess);
        Assert.Equal([sameNewerId, otherId], fromSeed.Value.Select(item => item.Id));

        var fromCategory = await handler.Handle(
            new GetLibraryRecommendationsQuery(CategoryId: health.Id.Value), CancellationToken.None);
        Assert.True(fromCategory.IsSuccess);
        Assert.Equal([sameNewerId, seedId, otherId], fromCategory.Value.Select(item => item.Id));

        var unknownSeed = await handler.Handle(
            new GetLibraryRecommendationsQuery(ContentId: Guid.NewGuid()), CancellationToken.None);
        Assert.True(unknownSeed.IsSuccess);
        Assert.Equal(otherId, unknownSeed.Value[0].Id); // top-viewed fallback first
    }

    [Fact]
    public async Task Admin_list_pages_and_filters_by_status_and_type()
    {
        await using CmsDbContext db = CreateCmsContext();
        LibraryContent published = await CreatePublished(db, "Article", "Published one");
        LibraryContent draft = await CreateDraft(db, "Video", "Draft video");
        await db.SaveChangesAsync();
        Guid publishedId = published.Id.Value;
        Guid draftId = draft.Id.Value;

        var handler = new GetLibraryAdminListQueryHandler(db);

        var all = await handler.Handle(new GetLibraryAdminListQuery(), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value.TotalCount);
        Assert.Equal(2, all.Value.Items.Count);

        var drafts = await handler.Handle(
            new GetLibraryAdminListQuery(Status: LibraryContentStatus.Draft), CancellationToken.None);
        Assert.Equal(draftId, Assert.Single(drafts.Value.Items).Id);

        var videos = await handler.Handle(
            new GetLibraryAdminListQuery(Type: LibraryContentType.Video), CancellationToken.None);
        Assert.Equal(draftId, Assert.Single(videos.Value.Items).Id);

        var search = await handler.Handle(
            new GetLibraryAdminListQuery(Search: "published one"), CancellationToken.None);
        Assert.Equal(publishedId, Assert.Single(search.Value.Items).Id);

        var paged = await handler.Handle(
            new GetLibraryAdminListQuery(Page: 2, PageSize: 1), CancellationToken.None);
        Assert.Single(paged.Value.Items);
        Assert.Equal(2, paged.Value.TotalCount);
    }

    [Fact]
    public async Task Public_content_reads_expose_the_cover_path_and_the_real_video_fields()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        LibraryCategory health = LibraryCategory.Create("صحة", "Health", true, DateTime.UtcNow);
        db.LibraryCategories.Add(health);
        await db.SaveChangesAsync();

        LibraryContent video = await CreatePublished(db, "Video", "Playable sample", health.Id.Value);
        LibraryContent article = await CreatePublished(db, "Article", "Reading sample", health.Id.Value);
        Guid videoId = video.Id.Value;
        Guid articleId = article.Id.Value;

        var list = await new GetLibraryContentListQueryHandler(db, families)
            .Handle(new GetLibraryContentListQuery(), CancellationToken.None);
        Assert.True(list.IsSuccess);

        LibraryContentListItemResponse videoItem =
            Assert.Single(list.Value, item => item.Id == videoId);
        Assert.Equal("Video", videoItem.Type);
        Assert.Equal("library/cover.png", videoItem.CoverImagePath);
        Assert.Equal(300, videoItem.DurationSeconds);
        Assert.Equal(health.Id.Value, videoItem.CategoryId);
        Assert.Equal("Health", videoItem.CategoryEnglishName);
        Assert.Equal("صحة", videoItem.CategoryArabicName);
        Assert.True(videoItem.CanAccess);

        LibraryContentListItemResponse articleItem =
            Assert.Single(list.Value, item => item.Id == articleId);
        Assert.Equal("Article", articleItem.Type);
        Assert.Null(articleItem.DurationSeconds);

        var recommendations = await new GetLibraryRecommendationsQueryHandler(db, families)
            .Handle(new GetLibraryRecommendationsQuery(ContentId: articleId), CancellationToken.None);
        Assert.True(recommendations.IsSuccess);
        LibraryContentListItemResponse recommended =
            Assert.Single(recommendations.Value, item => item.Id == videoId);
        Assert.Equal("library/cover.png", recommended.CoverImagePath);
        Assert.Equal(300, recommended.DurationSeconds);

        var detail = await new GetLibraryContentQueryHandler(db, families)
            .Handle(new GetLibraryContentQuery(videoId, Guid.NewGuid()), CancellationToken.None);
        Assert.True(detail.IsSuccess);
        Assert.Equal("https://youtu.be/abc123", detail.Value.VideoUrl);
        Assert.Equal(300, detail.Value.DurationSeconds);
        Assert.Equal("library/cover.png", detail.Value.CoverImagePath);
        Assert.Empty(detail.Value.Sections);
    }

    [Fact]
    public async Task Public_content_detail_returns_null_category_fields_when_the_content_has_no_category()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        LibraryContent content = await CreatePublished(db, "Article", "No category");
        Guid contentId = content.Id.Value;

        var detail = await new GetLibraryContentQueryHandler(db, families)
            .Handle(new GetLibraryContentQuery(contentId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Value.CategoryId);
        Assert.Null(detail.Value.CategoryArabicName);
        Assert.Null(detail.Value.CategoryEnglishName);
        Assert.False(string.IsNullOrWhiteSpace(detail.Value.CoverImagePath));
        Assert.NotEmpty(detail.Value.Sections);
    }

    [Fact]
    public async Task Premium_video_detail_keeps_the_real_duration_and_hides_the_video_url_without_the_benefit()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        LibraryContent premium = await CreatePublished(db, "Video", "Premium video", isPremium: true);
        Guid premiumId = premium.Id.Value;

        var detail = await new GetLibraryContentQueryHandler(db, families)
            .Handle(new GetLibraryContentQuery(premiumId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.True(detail.Value.IsPremium);
        Assert.False(detail.Value.CanAccess);
        Assert.Null(detail.Value.VideoUrl);
        Assert.Empty(detail.Value.Sections);
        // The teaser keeps the real stored duration and cover.
        Assert.Equal(300, detail.Value.DurationSeconds);
        Assert.Equal("library/cover.png", detail.Value.CoverImagePath);
    }

    private static Task<Result<bool>> SetSaved(
        CmsDbContext db, Guid contentId, UserId user, bool value) =>
        new SetLibraryContentSavedCommandHandler(db)
            .Handle(new SetLibraryContentSavedCommand(contentId, user.Value, value), CancellationToken.None);

    private static Task<Result<bool>> SetLiked(
        CmsDbContext db, Guid contentId, UserId user, bool value) =>
        new SetLibraryContentLikedCommandHandler(db)
            .Handle(new SetLibraryContentLikedCommand(contentId, user.Value, value), CancellationToken.None);

    [Fact]
    public async Task ContentList_WithViewer_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var familiesConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await familiesConnection.OpenAsync();
        await using var familiesDb = new FamiliesDbContext(
            new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(familiesConnection).Options);
        await familiesDb.Database.EnsureCreatedAsync();
        var viewer = UserId.New();
        var created = await new CreateLibraryContentCommandHandler(db).Handle(
            ArticleCommand(null) with { EnglishTitle = "Relational" }, CancellationToken.None);
        Assert.True(created.IsSuccess);
        var content = await db.LibraryContents.SingleAsync(item => item.Id == new LibraryContentId(created.Value.Id));
        content.Publish();
        await db.SaveChangesAsync();

        var list = await new GetLibraryContentListQueryHandler(db, familiesDb).Handle(
            new GetLibraryContentListQuery(ViewerUserId: viewer.Value), CancellationToken.None);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value);
    }

    private static CreateLibraryContentCommand ArticleCommand(Guid? categoryId) => new(
        LibraryContentType.Article,
        categoryId,
        "عنوان",
        "Title",
        "ملخص",
        "Summary",
        null,
        null,
        "library/cover.png",
        false,
        [
            new(1, "الأول", "First", ["نقطة"], ["bullet"]),
            new(2, "الثاني", "Second", ["نقطة"], ["bullet"])
        ]);

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

    private static async Task<LibraryContent> CreatePublished(
        CmsDbContext db, string type, string title, Guid? categoryId = null, bool isPremium = false)
    {
        LibraryContent content = await CreateDraft(db, type, title, categoryId, isPremium);
        content.Publish();
        await db.SaveChangesAsync();
        return content;
    }

    private static async Task<LibraryContent> CreateDraft(
        CmsDbContext db, string type, string title, Guid? categoryId = null, bool isPremium = false)
    {
        CreateLibraryContentCommand command = (type == "Video"
            ? VideoCommand()
            : ArticleCommand(null)) with
        {
            ArabicTitle = title,
            EnglishTitle = title,
            CategoryId = categoryId,
            IsPremium = isPremium
        };

        var result = await new CreateLibraryContentCommandHandler(db)
            .Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return db.LibraryContents.Single(item => item.Id == new LibraryContentId(result.Value.Id));
    }

    private static CmsDbContext CreateCmsContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
