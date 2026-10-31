using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Comments;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Community;

/// <summary>
/// FUR-07 Community-only timed author restrictions. A restriction blocks new
/// posts, comments, and replies until a UTC expiry; existing content and every
/// non-write interaction remain available. It never changes Identity status,
/// login, sessions, or access to any other module, and only Community write
/// commands consult it.
/// </summary>
public sealed class CommunityAuthorRestrictionTests
{
    [Theory]
    [InlineData(CommunityRestrictionDuration.Hours24, 24d)]
    [InlineData(CommunityRestrictionDuration.Days7, 7 * 24d)]
    [InlineData(CommunityRestrictionDuration.Days30, 30 * 24d)]
    public void Create_UsesFixedDurationPresets(
        CommunityRestrictionDuration duration,
        double expectedHours)
    {
        var startsOn = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

        var restriction = CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), duration,
            CommunityRestrictionReasonCodes.Spam, null, startsOn);

        Assert.Equal(startsOn, restriction.StartsOnUtc);
        Assert.Equal(startsOn.AddHours(expectedHours), restriction.ExpiresOnUtc);
        Assert.True(restriction.IsActiveAt(startsOn));
    }

    [Fact]
    public void IsActiveAt_RespectsExpiryAndRevocation()
    {
        var startsOn = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);
        var restriction = CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Hours24,
            CommunityRestrictionReasonCodes.Spam, null, startsOn);

        Assert.True(restriction.IsActiveAt(startsOn.AddHours(23)));
        Assert.False(restriction.IsActiveAt(startsOn.AddHours(24)));

        restriction.Revoke(UserId.New(), "appealed", startsOn.AddHours(2));
        Assert.False(restriction.IsActiveAt(startsOn.AddHours(3)));
    }

    [Fact]
    public void Create_RejectsUnknownReasonAndMissingOtherNote()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
            "Not/A/Real/Code", null, now));
        Assert.Throws<DomainException>(() => CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Other, "  ", now));
    }

    [Fact]
    public void Revoke_RequiresReasonAndRejectsSecondRevoke()
    {
        var startsOn = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);
        var restriction = CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Spam, null, startsOn);
        var moderator = UserId.New();

        Assert.Throws<DomainException>(() =>
            restriction.Revoke(moderator, "  ", startsOn.AddHours(1)));

        restriction.Revoke(moderator, "  mistake  ", startsOn.AddHours(1));
        Assert.Equal(moderator, restriction.RevokedBy);
        Assert.Equal(startsOn.AddHours(1), restriction.RevokedOnUtc);
        Assert.Equal("mistake", restriction.RevocationReason);

        Assert.Throws<DomainException>(() =>
            restriction.Revoke(moderator, "again", startsOn.AddHours(2)));
    }

    [Fact]
    public async Task CreateRestriction_PersistsAndReturnsActiveWithoutTouchingContent()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var moderator = UserId.New();

        var result = await new CreateCommunityAuthorRestrictionCommandHandler(db).Handle(
            new CreateCommunityAuthorRestrictionCommand(
                author.Value, moderator, nameof(CommunityRestrictionDuration.Days30),
                CommunityRestrictionReasonCodes.UnsafeContent, "unsafe"),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(author.Value, result.Value.AuthorId);
        Assert.Equal(moderator.Value, result.Value.ModeratorId);
        Assert.Equal(CommunityRestrictionDuration.Days30.ToString(), result.Value.Duration);
        Assert.Null(result.Value.RevokedOnUtc);

        CommunityAuthorRestriction stored = await db.CommunityAuthorRestrictions.SingleAsync();
        Assert.Equal(author, stored.AuthorId);
        // Community-only: nothing else in the Community schema is written.
        Assert.Empty(db.Posts);
        Assert.Empty(db.Comments);
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task CreateRestriction_SecondActive_ReturnsRestrictionAlreadyActive()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var handler = new CreateCommunityAuthorRestrictionCommandHandler(db);
        var command = new CreateCommunityAuthorRestrictionCommand(
            author.Value, UserId.New(), nameof(CommunityRestrictionDuration.Hours24),
            CommunityRestrictionReasonCodes.Spam, null);

        Assert.True((await handler.Handle(command, default)).IsSuccess);
        var second = await handler.Handle(command, default);

        Assert.True(second.IsFailure);
        Assert.Equal(CommunityModerationErrors.RestrictionAlreadyActive, second.Error);
        Assert.Equal(1, await db.CommunityAuthorRestrictions.CountAsync());
    }

    [Fact]
    public async Task CreateRestriction_ConcurrentRequestsOnlyPersistOneActiveRow()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(), $"ahlena-community-restriction-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30;Pooling=False")
            .Options;

        try
        {
            await using (var setup = new CommunityDbContext(options))
                await setup.Database.EnsureCreatedAsync();

            var author = UserId.New();
            await using var firstDb = new CommunityDbContext(options);
            await using var secondDb = new CommunityDbContext(options);
            var firstHandler = new CreateCommunityAuthorRestrictionCommandHandler(firstDb);
            var secondHandler = new CreateCommunityAuthorRestrictionCommandHandler(secondDb);

            Task<Result<CommunityAuthorRestrictionResponse>> first = firstHandler.Handle(
                NewRestrictionCommand(author), default);
            Task<Result<CommunityAuthorRestrictionResponse>> second = secondHandler.Handle(
                NewRestrictionCommand(author), default);
            Result<CommunityAuthorRestrictionResponse>[] results = await Task.WhenAll(first, second);

            Assert.Single(results, result => result.IsSuccess);
            Result<CommunityAuthorRestrictionResponse> conflict =
                Assert.Single(results, result => result.IsFailure);
            Assert.Equal(CommunityModerationErrors.RestrictionAlreadyActive, conflict.Error);

            await using var verify = new CommunityDbContext(options);
            Assert.Equal(1, await verify.CommunityAuthorRestrictions
                .CountAsync(restriction => restriction.AuthorId == author));
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("Not/Real")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateRestriction_InvalidDuration_ReturnsInvalidRestrictionDuration(string? duration)
    {
        await using var db = CreateDb();

        var result = await new CreateCommunityAuthorRestrictionCommandHandler(db).Handle(
            new CreateCommunityAuthorRestrictionCommand(
                UserId.New().Value, UserId.New(), duration,
                CommunityRestrictionReasonCodes.Spam, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidRestrictionDuration, result.Error);
    }

    [Fact]
    public async Task CreateRestriction_InvalidReason_ReturnsInvalidRestrictionReason()
    {
        await using var db = CreateDb();
        var handler = new CreateCommunityAuthorRestrictionCommandHandler(db);

        var unknown = await handler.Handle(new CreateCommunityAuthorRestrictionCommand(
            UserId.New().Value, UserId.New(), nameof(CommunityRestrictionDuration.Days7),
            "Not/A/Real/Code", null), default);
        var otherMissingNote = await handler.Handle(new CreateCommunityAuthorRestrictionCommand(
            UserId.New().Value, UserId.New(), nameof(CommunityRestrictionDuration.Days7),
            CommunityRestrictionReasonCodes.Other, "  "), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidRestrictionReason, unknown.Error);
        Assert.True(otherMissingNote.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidRestrictionReason, otherMissingNote.Error);
    }

    [Fact]
    public async Task Revoke_Unknown_ReturnsRestrictionNotFound()
    {
        await using var db = CreateDb();

        var result = await new RevokeCommunityAuthorRestrictionCommandHandler(db).Handle(
            new RevokeCommunityAuthorRestrictionCommand(
                Guid.CreateVersion7(), UserId.New(), "resolved"),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.RestrictionNotFound, result.Error);
    }

    [Fact]
    public async Task Revoke_BlankReason_ReturnsInvalidRevocationReason()
    {
        await using var db = CreateDb();
        var restriction = CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow);
        db.CommunityAuthorRestrictions.Add(restriction);
        await db.SaveChangesAsync();

        var result = await new RevokeCommunityAuthorRestrictionCommandHandler(db).Handle(
            new RevokeCommunityAuthorRestrictionCommand(
                restriction.Id.Value, UserId.New(), "   "),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidRevocationReason, result.Error);
        Assert.Null(restriction.RevokedOnUtc);
    }

    [Fact]
    public async Task Revoke_ActiveRestriction_RecordsAuditAndAllowsReplacement()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var moderator = UserId.New();
        var created = await new CreateCommunityAuthorRestrictionCommandHandler(db).Handle(
            new CreateCommunityAuthorRestrictionCommand(
                author.Value, moderator, nameof(CommunityRestrictionDuration.Days7),
                CommunityRestrictionReasonCodes.Spam, null),
            default);

        var revoked = await new RevokeCommunityAuthorRestrictionCommandHandler(db).Handle(
            new RevokeCommunityAuthorRestrictionCommand(
                created.Value.RestrictionId, moderator, "reduced to warning"),
            default);

        Assert.True(revoked.IsSuccess);
        Assert.False(revoked.Value.IsActive);
        Assert.Equal(moderator.Value, revoked.Value.RevokedBy);
        Assert.Equal("reduced to warning", revoked.Value.RevocationReason);
        Assert.False(await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
            db, author, DateTime.UtcNow, default));

        // The original action is retained; a fresh restriction can now be
        // added because the previous one is no longer active.
        Assert.Equal(1, await db.CommunityAuthorRestrictions.CountAsync());
        var second = await new CreateCommunityAuthorRestrictionCommandHandler(db).Handle(
            new CreateCommunityAuthorRestrictionCommand(
                author.Value, moderator, nameof(CommunityRestrictionDuration.Hours24),
                CommunityRestrictionReasonCodes.Spam, null),
            default);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_ReturnsRestrictionAlreadyRevoked()
    {
        await using var db = CreateDb();
        var restriction = CommunityAuthorRestriction.Create(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow);
        restriction.Revoke(UserId.New(), "first", DateTime.UtcNow);
        db.CommunityAuthorRestrictions.Add(restriction);
        await db.SaveChangesAsync();

        var result = await new RevokeCommunityAuthorRestrictionCommandHandler(db).Handle(
            new RevokeCommunityAuthorRestrictionCommand(
                restriction.Id.Value, UserId.New(), "second"),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.RestrictionAlreadyRevoked, result.Error);
    }

    [Fact]
    public async Task CreatePost_BlockedWhileRestricted()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        await AddActiveRestrictionAsync(db, author);

        var result = await new CreatePostCommandHandler(db).Handle(
            new CreatePostCommand
            {
                TitleArabic = "عنوان",
                TitleEnglish = "Title",
                ContentArabic = "محتوى",
                ContentEnglish = "Content",
                AuthorId = author.Value
            },
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.AuthorRestricted, result.Error);
        Assert.Empty(db.Posts);
    }

    [Fact]
    public async Task CreatePostComment_BlockedWhileRestricted()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        await AddActiveRestrictionAsync(db, author);

        var result = await new CreatePostCommentCommandHandler(db).Handle(
            new CreatePostCommentCommand
            {
                PostId = Guid.CreateVersion7(),
                AuthorId = author.Value,
                ContentArabic = "تعليق",
                ContentEnglish = "Comment"
            },
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.AuthorRestricted, result.Error);
        Assert.Empty(db.Comments);
    }

    [Fact]
    public async Task CreateComment_BlockedWhileRestricted()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        await AddActiveRestrictionAsync(db, author);

        var result = await new CreateCommentCommandHandler(db).Handle(
            new CreateCommentCommand
            {
                PostId = Guid.CreateVersion7(),
                AuthorId = author.Value,
                ContentArabic = "تعليق",
                ContentEnglish = "Comment"
            },
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.AuthorRestricted, result.Error);
        Assert.Empty(db.Comments);
    }

    [Fact]
    public async Task CreateReply_BlockedWhileRestricted()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        await AddActiveRestrictionAsync(db, author);

        var result = await new CreateReplyCommandHandler(db).Handle(
            new CreateReplyCommand
            {
                CommentId = Guid.CreateVersion7(),
                AuthorId = author.Value,
                ContentArabic = "رد",
                ContentEnglish = "Reply"
            },
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.AuthorRestricted, result.Error);
        Assert.Empty(db.Replies);
    }

    [Fact]
    public async Task CreatePost_AllowedWhenRestrictionExpiredOrRevoked()
    {
        await using var db = CreateDb();
        var expiredAuthor = UserId.New();
        db.CommunityAuthorRestrictions.Add(CommunityAuthorRestriction.Create(
            expiredAuthor, UserId.New(), CommunityRestrictionDuration.Days30,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow.AddDays(-31)));
        var revokedAuthor = UserId.New();
        var revoked = CommunityAuthorRestriction.Create(
            revokedAuthor, UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow);
        revoked.Revoke(UserId.New(), "appealed", DateTime.UtcNow);
        db.CommunityAuthorRestrictions.Add(revoked);
        await db.SaveChangesAsync();

        var expiredResult = await new CreatePostCommandHandler(db).Handle(
            NewPostCommand(expiredAuthor), default);
        var revokedResult = await new CreatePostCommandHandler(db).Handle(
            NewPostCommand(revokedAuthor), default);

        Assert.True(expiredResult.IsSuccess);
        Assert.True(revokedResult.IsSuccess);
        Assert.Equal(2, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task Guard_NoRestriction_DoesNotBlock()
    {
        await using var db = CreateDb();

        Assert.False(await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
            db, UserId.New(), DateTime.UtcNow, default));
        Assert.True((await new CreatePostCommandHandler(db).Handle(
            NewPostCommand(UserId.New()), default)).IsSuccess);
    }

    private static CreatePostCommand NewPostCommand(UserId author) => new()
    {
        TitleArabic = "عنوان",
        TitleEnglish = "Title",
        ContentArabic = "محتوى",
        ContentEnglish = "Content",
        AuthorId = author.Value
    };

    private static CreateCommunityAuthorRestrictionCommand NewRestrictionCommand(UserId author) => new(
        author.Value, UserId.New(), nameof(CommunityRestrictionDuration.Hours24),
        CommunityRestrictionReasonCodes.Spam, null);

    private static async Task AddActiveRestrictionAsync(CommunityDbContext db, UserId author)
    {
        db.CommunityAuthorRestrictions.Add(CommunityAuthorRestriction.Create(
            author, UserId.New(), CommunityRestrictionDuration.Hours24,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
