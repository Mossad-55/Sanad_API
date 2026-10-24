using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.CheckIns;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Application.Ratings;
using Sanad.Modules.Community.Application.Uploads;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Uploads;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

/// <summary>
/// FUR post management (Owner addition, locked 2026-10-23): author-only
/// edit/delete of posts, CommunityModeration deletion, deletion hiding and
/// child blocking, photo-privacy alignment, authorized image references and
/// the edit-vs-delete race guard. Handler-level boundaries; the real-HTTP,
/// wrong-role-denial and PostgreSQL race checks run in the Mastermind runtime
/// slot (authored here as the separate HTTP suite, see the API tests).
/// </summary>
public sealed class CommunityPostOwnershipTests
{
    // ---------------------------------------------------------- ownership

    [Fact]
    public async Task Author_edits_own_published_post_and_content_returns_to_review()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        DateTime publishedStamp = post.PublishedOnUtc!.Value;

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "عنوان جديد", "New Title", "محتوى جديد", "New content",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsSuccess);
        Post stored = await db.Posts.SingleAsync();

        // The edit updated the content fields...
        Assert.Equal("New Title", stored.TitleEnglish);
        Assert.Equal("New content", stored.ContentEnglish);
        Assert.NotNull(stored.UpdatedOnUtc);
        // ...and never bypassed moderation: the published post re-enters the
        // pending-review queue with its publication metadata cleared.
        Assert.Equal(PostStatus.PendingReview, stored.Status);
        Assert.Null(stored.PublishedBy);
        Assert.Null(stored.PublishedOnUtc);
        Assert.NotEqual(publishedStamp, stored.UpdatedOnUtc!.Value);
    }

    [Fact]
    public async Task Author_edits_own_pending_post_and_it_stays_pending()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "أ", "Edited", "ب", "Edited body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsSuccess);
        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.PendingReview, stored.Status);
        Assert.Equal("Edited", stored.TitleEnglish);
    }

    [Fact]
    public async Task Update_of_a_foreign_post_is_private_not_found_and_mutates_nothing()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, UserId.New().Value, // a stranger's claim
                "هجوم", "Hacked", "هجوم", "Hacked body",
                ImageUrl: null, IsAnonymous: true, CategoryId: null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.PostNotFound", result.Error.Code);

        Post stored = await db.Posts.SingleAsync();
        Assert.Equal("Title", stored.TitleEnglish); // unchanged content
        Assert.Equal(PostStatus.Published, stored.Status); // unchanged state
        Assert.False(stored.IsAnonymous); // unchanged anonymity
    }

    [Fact]
    public async Task Forging_an_author_or_post_id_cannot_widen_the_update_scope()
    {
        await using var db = CreateDb();
        var attacker = UserId.New();
        Post foreign = CreatePublishedPost(UserId.New());
        db.Posts.Add(foreign);
        await db.SaveChangesAsync();

        // The command carries only the acting (claim-derived) author id:
        // there is no author field a caller could forge. Attacking with a
        // foreign post id and the attacker's acting id answers the privacy
        // not-found and leaves the foreign post untouched.
        var crossPost = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                foreign.Id.Value, attacker.Value,
                "أ", "Swap", "ب", "Swap body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(crossPost.IsFailure);
        Assert.Equal("Community.PostNotFound", crossPost.Error.Code);
        Post stored = await db.Posts.AsNoTracking().SingleAsync();
        Assert.Equal("Title", stored.TitleEnglish);
        Assert.Equal(PostStatus.Published, stored.Status);
    }

    [Fact]
    public async Task Update_of_a_deleted_post_is_private_not_found()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        post.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "أ", "Zombie", "ب", "Zombie body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.PostNotFound", result.Error.Code);
        Assert.Equal(PostStatus.Deleted, (await db.Posts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Update_of_a_rejected_post_conflicts()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, DateTime.UtcNow);
        post.Reject(UserId.New(), CommunityRejectionReasonCodes.Spam, null, DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "أ", "Retry", "ب", "Retry body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsFailure);
        // The shared mapper maps this error family to 409.
        Assert.Equal("Community.InvalidModerationOperation", result.Error.Code);
        Assert.Equal(PostStatus.Rejected, (await db.Posts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Author_deletes_own_post_with_retained_audit_and_children()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        db.Comments.Add(Comment.Create(post.Id, UserId.New(), "تعليق", "Comment", DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        Assert.True(result.IsSuccess);
        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status);
        Assert.Equal(author, stored.DeletedBy);
        Assert.NotNull(stored.DeletedOnUtc);
        // Retained audit/moderation data: comments are NOT cascade-deleted
        // and the post row survives for the moderation history.
        Assert.Single(await db.Comments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Delete_of_a_foreign_post_is_private_not_found()
    {
        await using var db = CreateDb();
        Post post = CreatePublishedPost(UserId.New());
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, UserId.New().Value), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.PostNotFound", result.Error.Code);
        Assert.Equal(PostStatus.Published, (await db.Posts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Repeat_delete_and_delete_after_admin_delete_are_not_found()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var firstModerator = UserId.New();
        var adminDelete = await new AdminDeletePostCommandHandler(db).Handle(
            new AdminDeletePostCommand(post.Id.Value, firstModerator), default);
        Assert.True(adminDelete.IsSuccess);

        // The author route answers the same privacy 404 after an Admin
        // deletion — no existence disclosure either way.
        var authorDelete = await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);
        Assert.True(authorDelete.IsFailure);
        Assert.Equal("Community.PostNotFound", authorDelete.Error.Code);

        // A second moderation delete is equally a 404, and the FIRST
        // deletion's audit stays intact (no lost or overwritten deletion).
        var repeatAdminDelete = await new AdminDeletePostCommandHandler(db).Handle(
            new AdminDeletePostCommand(post.Id.Value, UserId.New()), default);
        Assert.True(repeatAdminDelete.IsFailure);
        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status);
        Assert.Equal(firstModerator, stored.DeletedBy);
    }

    [Fact]
    public async Task Moderator_deletes_another_authors_post_and_is_recorded()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var moderator = UserId.New();

        var result = await new AdminDeletePostCommandHandler(db).Handle(
            new AdminDeletePostCommand(post.Id.Value, moderator), default);

        Assert.True(result.IsSuccess);
        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status);
        Assert.Equal(moderator, stored.DeletedBy);
        Assert.NotEqual(author, stored.DeletedBy);
    }

    // -------------------------------------------------------- race guard

    [Fact]
    public async Task Author_edit_racing_an_admin_delete_does_not_resurrect_the_post()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseSqlite(connection).Options;
        await using var authorContext = new CommunityDbContext(options);
        await authorContext.Database.EnsureCreatedAsync();

        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        authorContext.Posts.Add(post);
        await authorContext.SaveChangesAsync(); // entity stays tracked, later stale

        // The Admin deletes through a DIFFERENT context (the race window).
        await using var adminContext = new CommunityDbContext(options);
        var adminDelete = await new AdminDeletePostCommandHandler(adminContext).Handle(
            new AdminDeletePostCommand(post.Id.Value, UserId.New()), default);
        Assert.True(adminDelete.IsSuccess);

        // The author's now-stale edit must not resurrect the deleted post:
        // the lifecycle concurrency token fails the save with the 409 family.
        var authorEdit = await new UpdatePostCommandHandler(authorContext).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "أ", "Zombie", "ب", "Zombie body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(authorEdit.IsFailure);
        Assert.Equal("Community.InvalidModerationOperation", authorEdit.Error.Code);

        await using var fresh = new CommunityDbContext(options);
        Post stored = await fresh.Posts.AsNoTracking().SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status); // deletion intact
    }

    // ----------------------------------------------------- deleted hiding

    [Fact]
    public async Task A_deleted_post_disappears_from_feed_search_detail_and_favorites()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var viewer = UserId.New();
        Post kept = CreatePublishedPost(author);
        Post deleted = CreatePublishedPost(author);
        db.Posts.AddRange(kept, deleted);
        db.Interactions.Add(CommunityInteraction.Create(
            CommunityInteractionTarget.Post, deleted.Id.Value, viewer,
            CommunityInteractionKind.Favorite, DateTime.UtcNow));
        await db.SaveChangesAsync();
        deleted.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var feed = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, ViewerUserId: viewer.Value), default);
        Assert.True(feed.IsSuccess);
        var feedItem = Assert.Single(feed.Value);
        Assert.Equal(kept.Id, feedItem.Post.Id);

        var search = await new GetPostsQueryHandler(db).Handle(
            new GetPostsQuery(PostStatus.Published, Search: "Title"), default);
        Assert.True(search.IsSuccess);
        var searchItem = Assert.Single(search.Value);
        Assert.Equal(kept.Id, searchItem.Post.Id);

        var detail = await new GetPostQueryHandler(db).Handle(
            new GetPostQuery(deleted.Id.Value, viewer.Value), default);
        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Value);

        var favorites = await new GetFavoritePostsQueryHandler(db).Handle(
            new GetFavoritePostsQuery(viewer.Value), default);
        Assert.True(favorites.IsSuccess);
        Assert.Empty(favorites.Value);
    }

    [Fact]
    public async Task A_deleted_post_disappears_from_recommendations()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CommunityDbContext(
            new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var viewer = UserId.New();
        var author = UserId.New();
        Post kept = CreatePublishedPost(author);
        Post deleted = CreatePublishedPost(author);
        db.Posts.AddRange(kept, deleted);
        await db.SaveChangesAsync();
        deleted.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var result = await db.GetRecommendedPostsAsync(viewer, 1, 10);

        Post single = Assert.Single(result);
        Assert.Equal(kept.Id, single.Id);
    }

    // ------------------------------------------------------- child writes

    [Fact]
    public async Task Comments_and_replies_cannot_continue_on_a_deleted_post()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        Comment comment = Comment.Create(post.Id, UserId.New(), "تعليق", "Comment", DateTime.UtcNow);
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        post.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var commentCreate = await new CreatePostCommentCommandHandler(db).Handle(
            new CreatePostCommentCommand
            {
                PostId = post.Id.Value,
                AuthorId = UserId.New().Value,
                ContentArabic = "جديد",
                ContentEnglish = "New comment",
            }, default);
        Assert.True(commentCreate.IsFailure);
        Assert.Equal("Community.PostNotFound", commentCreate.Error.Code);

        var replyCreate = await new CreateReplyCommandHandler(db).Handle(
            new CreateReplyCommand
            {
                CommentId = comment.Id.Value,
                AuthorId = UserId.New().Value,
                ContentArabic = "رد",
                ContentEnglish = "New reply",
            }, default);
        Assert.True(replyCreate.IsFailure);
        Assert.Equal("Community.PostNotFound", replyCreate.Error.Code);
    }

    [Fact]
    public async Task A_deleted_posts_thread_is_hidden_from_comment_and_reply_reads()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post deleted = CreatePublishedPost(author);
        Post kept = CreatePublishedPost(author);
        db.Posts.AddRange(deleted, kept);
        Comment deletedComment = Comment.Create(deleted.Id, UserId.New(), "محذوف", "Deleted thread", DateTime.UtcNow);
        Reply deletedReply = Reply.Create(deletedComment.Id, UserId.New(), "محذوف", "Deleted reply", DateTime.UtcNow);
        Comment keptComment = Comment.Create(kept.Id, UserId.New(), "باق", "Kept thread", DateTime.UtcNow);
        db.Comments.AddRange(deletedComment, keptComment);
        db.Replies.Add(deletedReply);
        await db.SaveChangesAsync();
        deleted.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var comments = await new GetCommentsQueryHandler(db).Handle(
            new GetCommentsQuery(deleted.Id.Value, 1, 20), default);
        Assert.True(comments.IsSuccess);
        Assert.Empty(comments.Value);

        var paged = await new GetPostCommentsQueryHandler(db).Handle(
            new GetPostCommentsQuery(deleted.Id.Value, 1, 20), default);
        Assert.True(paged.IsSuccess);
        Assert.Equal(0, paged.Value.TotalCount);
        Assert.Empty(paged.Value.Items);

        var replies = await new GetCommentRepliesQueryHandler(db).Handle(
            new GetCommentRepliesQuery(deletedComment.Id.Value, 1, 20), default);
        Assert.True(replies.IsSuccess);
        Assert.Empty(replies.Value);

        // The surviving post's thread is untouched.
        var keptComments = await new GetCommentsQueryHandler(db).Handle(
            new GetCommentsQuery(kept.Id.Value, 1, 20), default);
        Comment keptItem = Assert.Single(keptComments.Value);
        Assert.Equal(keptComment.Id, keptItem.Id);
    }

    // --------------------------------------------------------------- media

    [Fact]
    public async Task Deleting_a_post_never_removes_the_shared_media_asset()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        CommunityImage image = CommunityImage.Create(
            "private/community/some-image.jpg", "image/jpeg", 1_024,
            author, DateTime.UtcNow);
        db.CommunityImages.Add(image);
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content",
            CommunityImageUrls.For(image.Id), false, author, DateTime.UtcNow);
        post.Publish(UserId.New(), DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        // The image row and its storage key survive: deleting one post must
        // never remove another record's media or a possibly shared asset.
        CommunityImage storedImage = await db.CommunityImages.SingleAsync();
        Assert.Equal(image.StorageKey, storedImage.StorageKey);
        Assert.Equal(image.ContentType, storedImage.ContentType);
    }

    [Fact]
    public async Task Create_rejects_arbitrary_external_image_urls()
    {
        await using var db = CreateDb();

        var result = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "عنوان",
            TitleEnglish = "Title",
            ContentArabic = "محتوى",
            ContentEnglish = "Content",
            AuthorId = UserId.New().Value,
            ImageUrl = "https://example.com/videos/external.jpg",
        }, default);

        Assert.True(result.IsFailure);
        // The shared mapper maps this code to 400.
        Assert.Equal("Community.Image.Invalid", result.Error.Code);
        Assert.Empty(await db.Posts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Create_rejects_another_users_private_upload()
    {
        await using var db = CreateDb();
        CommunityImage foreignPrivate = CommunityImage.Create(
            "private/community/foreign.jpg", "image/jpeg", 1_024,
            UserId.New(), DateTime.UtcNow);
        db.CommunityImages.Add(foreignPrivate);
        await db.SaveChangesAsync();

        var result = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "عنوان",
            TitleEnglish = "Title",
            ContentArabic = "محتوى",
            ContentEnglish = "Content",
            AuthorId = UserId.New().Value,
            ImageUrl = CommunityImageUrls.For(foreignPrivate.Id),
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.Image.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task Create_accepts_the_authors_own_upload_and_an_already_published_asset()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        CommunityImage own = CommunityImage.Create(
            "private/community/own.jpg", "image/jpeg", 1_024, author, DateTime.UtcNow);
        var publisher = UserId.New();
        CommunityImage publicAsset = CommunityImage.Create(
            "private/community/published.jpg", "image/jpeg", 1_024, publisher, DateTime.UtcNow);
        db.CommunityImages.AddRange(own, publicAsset);
        Post publishedElsewhere = Post.Create(
            "أ", "Other", "ب", "Other body", CommunityImageUrls.For(publicAsset.Id),
            false, publisher, DateTime.UtcNow);
        publishedElsewhere.Publish(UserId.New(), DateTime.UtcNow);
        db.Posts.Add(publishedElsewhere);
        await db.SaveChangesAsync();

        var ownResult = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "عنوان",
            TitleEnglish = "Title",
            ContentArabic = "محتوى",
            ContentEnglish = "Content",
            AuthorId = author.Value,
            ImageUrl = CommunityImageUrls.For(own.Id),
        }, default);

        Assert.True(ownResult.IsSuccess);
        Post ownPost = ownResult.Value!;
        Assert.Equal(CommunityImageUrls.For(own.Id), ownPost.ImageUrl);

        // A stranger may reference an asset that is already public on a
        // published post — exactly the visibility the image read route grants.
        var publicResult = await new CreatePostCommandHandler(db).Handle(new CreatePostCommand
        {
            TitleArabic = "عنوان",
            TitleEnglish = "Second",
            ContentArabic = "محتوى",
            ContentEnglish = "Second body",
            AuthorId = author.Value,
            ImageUrl = CommunityImageUrls.For(publicAsset.Id),
        }, default);

        Assert.True(publicResult.IsSuccess);
        Post publicPost = publicResult.Value!;
        Assert.Equal(CommunityImageUrls.For(publicAsset.Id), publicPost.ImageUrl);
    }

    [Fact]
    public async Task Update_to_a_foreign_private_asset_is_rejected_and_keeps_the_old_image()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        CommunityImage own = CommunityImage.Create(
            "private/community/own.jpg", "image/jpeg", 1_024, author, DateTime.UtcNow);
        CommunityImage foreignPrivate = CommunityImage.Create(
            "private/community/foreign.jpg", "image/jpeg", 1_024, UserId.New(), DateTime.UtcNow);
        db.CommunityImages.AddRange(own, foreignPrivate);
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content",
            CommunityImageUrls.For(own.Id), false, author, DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "عنوان", "Title", "محتوى", "Content",
                ImageUrl: CommunityImageUrls.For(foreignPrivate.Id),
                IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.Image.Invalid", result.Error.Code);
        Assert.Equal(CommunityImageUrls.For(own.Id), (await db.Posts.SingleAsync()).ImageUrl);
    }

    [Fact]
    public async Task Update_keeps_an_unchanged_legacy_external_image_without_validation()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        string legacy = "https://example.com/legacy-banner.jpg";
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", legacy, false, author, DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        // Pre-upload-enforcement posts keep editing as long as the author does
        // not change the image value: the reference they did not touch is not
        // re-validated, so no unrelated edit is blocked by a historical value.
        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "عنوان", "Edited", "محتوى", "Edited body",
                ImageUrl: legacy, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(legacy, (await db.Posts.SingleAsync()).ImageUrl);
    }

    [Fact]
    public async Task Update_can_clear_the_image()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content",
            "https://example.com/legacy-banner.jpg", false, author, DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new UpdatePostCommandHandler(db).Handle(
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "عنوان", "Edited", "محتوى", "Edited body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        Assert.True(result.IsSuccess);
        Post stored = await db.Posts.SingleAsync();
        Assert.Null(stored.ImageUrl);
    }

    // --------------------------------------------------- photo consistency

    [Fact]
    public async Task Author_photo_path_follows_the_download_route_across_projections()
    {
        await using var harness = await CommunityAuthorProjectionHarness.CreateAsync();

        // Served author: a published non-anonymous post and a stored avatar.
        var servable = UserId.New();
        await harness.AddIdentityUserAsync(
            servable.Value, "الشخص", "Servable Author", avatarKey: "private/avatars/servable.jpg");
        await harness.AddPublishedPostAsync(servable.Value, isAnonymous: false);

        // Anonymous history suppresses the photo everywhere (D7) while the
        // display name of the visible post stays.
        var anonymousHistory = UserId.New();
        await harness.AddIdentityUserAsync(
            anonymousHistory.Value, "المخفي", "Hidden Author", avatarKey: "private/avatars/hidden.jpg");
        await harness.AddPublishedPostAsync(anonymousHistory.Value, isAnonymous: false);
        await harness.AddPublishedPostAsync(anonymousHistory.Value, isAnonymous: true);

        // No published, non-anonymous post at all → nothing is served.
        var pendingOnly = UserId.New();
        await harness.AddIdentityUserAsync(
            pendingOnly.Value, "بالانتظار", "Pending Author", avatarKey: "private/avatars/pending.jpg");
        Post pendingPost = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false,
            pendingOnly, DateTime.UtcNow);
        harness.Context.Posts.Add(pendingPost);
        await harness.Context.SaveChangesAsync();

        // Only published posts, all of them deleted → nothing is served.
        var deletedOnly = UserId.New();
        await harness.AddIdentityUserAsync(
            deletedOnly.Value, "المحذوف", "Deleted Author", avatarKey: "private/avatars/deleted.jpg");
        Guid deletedOnlyPost = await harness.AddPublishedPostAsync(deletedOnly.Value, isAnonymous: false);
        await new AdminDeletePostCommandHandler(harness.Context).Handle(
            new AdminDeletePostCommand(deletedOnlyPost, UserId.New()), default);

        var profiles = await new GetCommunityAuthorProfilesQueryHandler(harness.Context).Handle(
            new GetCommunityAuthorProfilesQuery(
                [servable.Value, anonymousHistory.Value, pendingOnly.Value, deletedOnly.Value]),
            default);

        Assert.True(profiles.IsSuccess);
        Assert.Equal(
            $"/api/v1/community/authors/{servable.Value}/photo",
            profiles.Value[servable.Value].AvatarUrl);
        Assert.Null(profiles.Value[anonymousHistory.Value].AvatarUrl);
        Assert.Equal("Hidden Author", profiles.Value[anonymousHistory.Value].DisplayName);
        Assert.Null(profiles.Value[pendingOnly.Value].AvatarUrl);
        Assert.Null(profiles.Value[deletedOnly.Value].AvatarUrl);

        // Deleting the servable author's post revokes the photo path on the
        // next read — never a URL the download route answers 404 for.
        var feed = await new GetPostsQueryHandler(harness.Context).Handle(
            new GetPostsQuery(PostStatus.Published, ViewerUserId: servable.Value), default);
        Guid servablePost = Assert.Single(
            feed.Value, item => item.Post.AuthorId.Value == servable.Value).Post.Id.Value;
        await new AdminDeletePostCommandHandler(harness.Context).Handle(
            new AdminDeletePostCommand(servablePost, UserId.New()), default);

        var afterDeletion = await new GetCommunityAuthorProfilesQueryHandler(harness.Context).Handle(
            new GetCommunityAuthorProfilesQuery([servable.Value]), default);
        Assert.True(afterDeletion.IsSuccess);
        Assert.Null(afterDeletion.Value[servable.Value].AvatarUrl);
    }

    // --------------------------------------- reactions stop after deletion
    // (testing-contract correction, locked 2026-10-23: every mutation that
    // starts or continues a post reaction rejects a deleted parent)

    [Fact]
    public async Task Like_and_favorite_are_rejected_on_a_deleted_post()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        var like = await new LikePostCommandHandler(db).Handle(
            new LikePostCommand { PostId = post.Id.Value, UserId = UserId.New().Value },
            default);
        Assert.True(like.IsFailure);
        Assert.Equal("Community.PostNotFound", like.Error.Code);

        var favorite = await new ToggleFavoritePostCommandHandler(db).Handle(
            new ToggleFavoritePostCommand(post.Id.Value, UserId.New().Value),
            default);
        Assert.True(favorite.IsFailure);
        Assert.Equal("Community.PostNotFound", favorite.Error.Code);

        // Nothing persisted: no interaction rows and no counter movement.
        Assert.Empty(await db.Interactions.AsNoTracking().ToListAsync());
        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(0, stored.LikesCount);
        Assert.Equal(0, stored.FavoritesCount);
    }

    [Fact]
    public async Task Unlike_on_a_deleted_post_is_rejected_and_the_retained_like_stays()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        // The like existed before deletion; deletion retains it.
        var liker = UserId.New();
        var like = await new LikePostCommandHandler(db).Handle(
            new LikePostCommand { PostId = post.Id.Value, UserId = liker.Value },
            default);
        Assert.True(like.IsSuccess && like.Value);

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        // No cleanup semantics exist in the deletion contract: an unlike
        // against the deleted parent is the same privacy rejection and the
        // retained interaction row keeps its deletion-time state.
        var unlike = await new UnlikePostCommandHandler(db).Handle(
            new UnlikePostCommand(post.Id.Value, liker.Value),
            default);
        Assert.True(unlike.IsFailure);
        Assert.Equal("Community.PostNotFound", unlike.Error.Code);

        Assert.Single(await db.Interactions.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await db.Posts.SingleAsync()).LikesCount);
    }

    [Fact]
    public async Task Comment_like_toggle_stops_when_the_parent_post_is_deleted()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var likeWhilePublished = await new LikeCommunityCommentCommandHandler(db).Handle(
            new LikeCommunityCommentCommand(
                (await new CreatePostCommentCommandHandler(db).Handle(
                    new CreatePostCommentCommand
                    {
                        PostId = post.Id.Value,
                        AuthorId = UserId.New().Value,
                        ContentArabic = "تعليق",
                        ContentEnglish = "Comment",
                    }, default)).Value!.Id.Value,
                UserId.New().Value),
            default);
        Assert.True(likeWhilePublished.IsSuccess && likeWhilePublished.Value);
        Assert.Equal(1, (await db.Comments.SingleAsync()).LikesCount);

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        // Neither a new like nor the undo continues against the deleted
        // parent; the wire not-found shape and the retained row both stand.
        var likeAfterDeletion = await new LikeCommunityCommentCommandHandler(db).Handle(
            new LikeCommunityCommentCommand((await db.Comments.SingleAsync()).Id.Value, UserId.New().Value),
            default);
        Assert.True(likeAfterDeletion.IsSuccess);
        Assert.False(likeAfterDeletion.Value); // controller maps this to 404

        Assert.Single(await db.Interactions.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await db.Comments.SingleAsync()).LikesCount);
    }

    [Fact]
    public async Task Comment_like_is_blocked_for_a_not_yet_published_parent()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post pending = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, DateTime.UtcNow);
        db.Posts.Add(pending);
        await db.SaveChangesAsync();

        Comment comment = Comment.Create(
            pending.Id, UserId.New(), "تعليق", "Comment", DateTime.UtcNow);
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        var result = await new LikeCommunityCommentCommandHandler(db).Handle(
            new LikeCommunityCommentCommand(comment.Id.Value, UserId.New().Value),
            default);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value); // still-Published rule, controller → 404
        Assert.Empty(await db.Interactions.AsNoTracking().ToListAsync());
        Assert.Equal(0, (await db.Comments.SingleAsync()).LikesCount);
    }

    [Fact]
    public async Task Rating_and_check_in_mutations_stop_on_a_deleted_post()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        // Rating already published-only before this correction; the deleted
        // parent is the same non-published rejection.
        var rating = await new AddRatingCommandHandler(db).Handle(
            new AddRatingCommand { PostId = post.Id.Value, UserId = UserId.New().Value, Value = 5 },
            default);
        Assert.True(rating.IsSuccess);
        Assert.False(rating.Value); // controller maps this to 404
        Assert.Empty(await db.Ratings.AsNoTracking().ToListAsync());

        // Check-in already answered the privacy not-found for non-published.
        var checkIn = await new AddCheckInCommandHandler(db).Handle(
            new AddCheckInCommand
            {
                PostId = post.Id.Value,
                UserId = UserId.New().Value,
                Answer = true,
                TimeZoneId = "UTC",
                LocalDate = DateOnly.FromDateTime(DateTime.UtcNow),
                AnsweredAtLocalTime = new TimeOnly(9, 0),
            },
            default);
        Assert.True(checkIn.IsFailure);
        Assert.Equal("Community.PostNotFound", checkIn.Error.Code);
        Assert.Empty(await db.CheckIns.AsNoTracking().ToListAsync());
    }

    // ------------------------------------------------------------- helpers

    private static Post CreatePublishedPost(UserId author)
    {
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, DateTime.UtcNow);
        post.Publish(UserId.New(), DateTime.UtcNow);
        return post;
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
