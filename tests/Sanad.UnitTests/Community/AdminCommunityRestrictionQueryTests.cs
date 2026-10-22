using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Blocks;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Uploads;
using Sanad.Modules.Community.Infrastructure.Persistence;

namespace Sanad.UnitTests.Community;

/// <summary>
/// F4 retained-restriction read contract. The read reports the persisted
/// restriction/revocation facts plus the create/revoke wire duration and reason
/// codes, resolves author display names through one batched identity lookup,
/// and derives "active" from the same persisted facts as the Community write
/// gate, so a read can never disagree with the gate. Active/expired/revoked/
/// future-start rows, both filters, stable paging with the actual filtered
/// total, absent author names and empty reads are covered here.
/// </summary>
public sealed class AdminCommunityRestrictionQueryTests
{
    private static readonly DateTime SeedBaseUtc = new(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handler_PagesNewestFirstWithActualFilteredTotal()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        List<CommunityAuthorRestriction> seeded = await db.SeedDescendingStartsAsync(5);

        var handler = new GetAdminCommunityRestrictionsQueryHandler(db.Context);
        var first = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: 1, PageSize: 2), default);
        var second = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: 2, PageSize: 2), default);
        var third = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: 3, PageSize: 2), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Value.Items.Count);
        Assert.Equal(2, second.Value.Items.Count);
        Assert.Single(third.Value.Items);
        Assert.Equal(5, first.Value.TotalCount);
        Assert.Equal(5, second.Value.TotalCount);
        Assert.Equal(5, third.Value.TotalCount);

        Guid[] expectedOrder = seeded
            .OrderByDescending(r => r.StartsOnUtc)
            .Select(r => r.Id.Value)
            .ToArray();
        Guid[] actualOrder =
        [
            .. first.Value.Items.Select(item => item.RestrictionId),
            .. second.Value.Items.Select(item => item.RestrictionId),
            .. third.Value.Items.Select(item => item.RestrictionId)
        ];
        Assert.Equal(expectedOrder, actualOrder);
    }

    [Fact]
    public async Task Handler_ClampsPageAndPageSizeToExistingModerationConventions()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        await db.SeedDescendingStartsAsync(1);
        var handler = new GetAdminCommunityRestrictionsQueryHandler(db.Context);

        var defaults = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: 0, PageSize: 0), default);
        var negative = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: -4, PageSize: -10), default);
        var oversized = await handler.Handle(new GetAdminCommunityRestrictionsQuery(PageSize: 500), default);

        Assert.Equal(1, defaults.Value.Page);
        Assert.Equal(25, defaults.Value.PageSize);
        Assert.Equal(1, negative.Value.Page);
        Assert.Equal(25, negative.Value.PageSize);
        Assert.Equal(100, oversized.Value.PageSize);
    }

    [Fact]
    public async Task Handler_AuthorFilterNarrowsItemsAndTotal()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        var author = UserId.New();
        await db.AddAuthorAsync(author, "مؤلف", "Author");
        await db.AddRestrictionAsync(author, UserId.New(), CommunityRestrictionDuration.Days7);
        await db.AddRestrictionAsync(UserId.New(), UserId.New(), CommunityRestrictionDuration.Hours24);
        await db.AddRestrictionAsync(UserId.New(), UserId.New(), CommunityRestrictionDuration.Days30);

        var result = await new GetAdminCommunityRestrictionsQueryHandler(db.Context).Handle(
            new GetAdminCommunityRestrictionsQuery(AuthorId: author.Value), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        AdminCommunityRestrictionListItem item = Assert.Single(result.Value.Items);
        Assert.Equal(author.Value, item.AuthorId);
        Assert.Equal("مؤلف", item.AuthorArabicName);
        Assert.Equal("Author", item.AuthorEnglishName);
    }

    [Fact]
    public async Task Handler_ActiveFilterAgreesWithWriteGateForEveryRowShape()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        UserId active = UserId.New();
        UserId alsoActive = UserId.New();
        UserId expired = UserId.New();
        UserId revoked = UserId.New();
        UserId futureStart = UserId.New();

        await db.AddRestrictionAsync(active, UserId.New(), CommunityRestrictionDuration.Days7);
        await db.AddRestrictionAsync(alsoActive, UserId.New(), CommunityRestrictionDuration.Days30);
        await db.AddRestrictionAsync(
            expired, UserId.New(), CommunityRestrictionDuration.Days30,
            startsOnUtc: DateTime.UtcNow.AddDays(-31));
        CommunityAuthorRestriction revokedRow = await db.AddRestrictionAsync(
            revoked, UserId.New(), CommunityRestrictionDuration.Hours24,
            startsOnUtc: DateTime.UtcNow.AddHours(-1));
        revokedRow.Revoke(UserId.New(), "appealed", DateTime.UtcNow);
        await db.Context.SaveChangesAsync();
        await db.AddRestrictionAsync(
            futureStart, UserId.New(), CommunityRestrictionDuration.Days7,
            startsOnUtc: DateTime.UtcNow.AddHours(3));

        var handler = new GetAdminCommunityRestrictionsQueryHandler(db.Context);
        var activePage = await handler.Handle(new GetAdminCommunityRestrictionsQuery(IsActive: true), default);
        var inactivePage = await handler.Handle(new GetAdminCommunityRestrictionsQuery(IsActive: false), default);

        UserId[] seededAuthors = [active, alsoActive, expired, revoked, futureStart];
        foreach (UserId author in seededAuthors)
        {
            bool gateBlocks = await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
                db.Context, author, DateTime.UtcNow, default);
            bool listedActive = activePage.Value.Items.Any(item => item.AuthorId == author.Value);
            bool listedInactive = inactivePage.Value.Items.Any(item => item.AuthorId == author.Value);

            Assert.True(
                gateBlocks == listedActive,
                $"The active filter disagreed with the write gate for {author.Value}: gate={gateBlocks}, listed={listedActive}.");
            Assert.True(
                !gateBlocks == listedInactive,
                $"The inactive filter disagreed with the write gate for {author.Value}: gate={gateBlocks}, listedInactive={listedInactive}.");
        }

        // An author with no retained restriction belongs to neither page and is
        // never reported active or inactive.
        UserId unknownAuthor = UserId.New();
        Assert.DoesNotContain(activePage.Value.Items, item => item.AuthorId == unknownAuthor.Value);
        Assert.DoesNotContain(inactivePage.Value.Items, item => item.AuthorId == unknownAuthor.Value);

        AdminCommunityRestrictionListItem declaredActive = Assert.Single(
            activePage.Value.Items, item => item.AuthorId == active.Value);
        Assert.Null(declaredActive.RevokedOnUtc);
        Assert.True(declaredActive.IsActive);

        // A stored start instant that is still in the future is reported, and
        // the row counts as active, exactly like the domain and the write gate.
        AdminCommunityRestrictionListItem declaredFuture = Assert.Single(
            activePage.Value.Items, item => item.AuthorId == futureStart.Value);
        Assert.True(declaredFuture.StartsOnUtc > DateTime.UtcNow);
        Assert.True(declaredFuture.IsActive);

        AdminCommunityRestrictionListItem declaredExpired = Assert.Single(
            inactivePage.Value.Items, item => item.AuthorId == expired.Value);
        AdminCommunityRestrictionListItem declaredRevoked = Assert.Single(
            inactivePage.Value.Items, item => item.AuthorId == revoked.Value);
        Assert.False(declaredExpired.IsActive);
        Assert.False(declaredRevoked.IsActive);
        Assert.Equal(5, activePage.Value.TotalCount + inactivePage.Value.TotalCount);
    }

    [Fact]
    public async Task ActiveFilterExtensions_PartitionTheRowsExactlyLikeTheDomainPredicate()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        UserId expired = UserId.New();
        UserId revokedRow = UserId.New();
        await db.AddRestrictionAsync(UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7);
        await db.AddRestrictionAsync(
            expired, UserId.New(), CommunityRestrictionDuration.Hours24,
            startsOnUtc: DateTime.UtcNow.AddHours(-25));
        CommunityAuthorRestriction revoked = await db.AddRestrictionAsync(
            revokedRow, UserId.New(), CommunityRestrictionDuration.Days30,
            startsOnUtc: DateTime.UtcNow.AddHours(-2));
        revoked.Revoke(UserId.New(), "appealed", DateTime.UtcNow.AddHours(-1));
        await db.Context.SaveChangesAsync();

        DateTime now = DateTime.UtcNow;
        List<CommunityAuthorRestriction> all = await db.Context.CommunityAuthorRestrictions
            .AsNoTracking()
            .ToListAsync();
        HashSet<CommunityAuthorRestrictionId> active = (await db.Context.CommunityAuthorRestrictions
                .AsNoTracking()
                .ActiveAt(now)
                .ToListAsync())
            .Select(row => row.Id)
            .ToHashSet();
        HashSet<CommunityAuthorRestrictionId> inactive = (await db.Context.CommunityAuthorRestrictions
                .AsNoTracking()
                .NotActiveAt(now)
                .ToListAsync())
            .Select(row => row.Id)
            .ToHashSet();

        foreach (CommunityAuthorRestriction row in all)
        {
            Assert.Equal(row.IsActiveAt(now), active.Contains(row.Id));
            Assert.Equal(!row.IsActiveAt(now), inactive.Contains(row.Id));
        }

        Assert.Equal(3, all.Count);
        Assert.Equal(all.Count, active.Count + inactive.Count);
        Assert.Empty(active.Intersect(inactive));
        Assert.Single(active);
        Assert.Equal(2, inactive.Count);
    }

    [Fact]
    public async Task Handler_ReportsRetainedFactsAndRevocationAudit()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        var author = UserId.New();
        var moderator = UserId.New();
        var revoker = UserId.New();
        CommunityAuthorRestriction restriction = await db.AddRestrictionAsync(
            author, moderator, CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.UnsafeContent, "unsafe content",
            DateTime.UtcNow.AddHours(-2));
        restriction.Revoke(revoker, "  appealed and reduced  ", DateTime.UtcNow.AddHours(-1));
        await db.Context.SaveChangesAsync();

        var result = await new GetAdminCommunityRestrictionsQueryHandler(db.Context).Handle(
            new GetAdminCommunityRestrictionsQuery(AuthorId: author.Value), default);

        AdminCommunityRestrictionListItem item = Assert.Single(result.Value.Items);
        Assert.Equal(restriction.Id.Value, item.RestrictionId);
        Assert.Equal(author.Value, item.AuthorId);
        Assert.Equal(moderator.Value, item.ModeratorId);
        Assert.Equal(nameof(CommunityRestrictionDuration.Days7), item.Duration);
        Assert.Equal(CommunityRestrictionReasonCodes.UnsafeContent, item.ReasonCode);
        Assert.Equal("unsafe content", item.Note);
        Assert.Equal(restriction.StartsOnUtc, item.StartsOnUtc);
        Assert.Equal(restriction.ExpiresOnUtc, item.ExpiresOnUtc);
        Assert.False(item.IsActive);
        Assert.Equal(revoker.Value, item.RevokedBy);
        Assert.Equal(restriction.RevokedOnUtc, item.RevokedOnUtc);
        Assert.Equal("appealed and reduced", item.RevocationReason);
    }

    [Fact]
    public async Task Handler_ResolvesAuthorNamesThroughOneBatchedLookupForThePage()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        UserId first = UserId.New();
        UserId second = UserId.New();
        await db.AddAuthorAsync(first, "الأول", "First");
        await db.AddAuthorAsync(second, "الثاني", "Second");
        CommunityAuthorRestriction firstRow = await db.AddRestrictionAsync(
            first, UserId.New(), CommunityRestrictionDuration.Days7);
        CommunityAuthorRestriction secondRow = await db.AddRestrictionAsync(
            second, UserId.New(), CommunityRestrictionDuration.Days30);
        // A second row for the same author must not be looked up twice.
        await db.AddRestrictionAsync(first, UserId.New(), CommunityRestrictionDuration.Hours24);

        var counting = new CountingCommunityDbContext(db.Context);
        var result = await new GetAdminCommunityRestrictionsQueryHandler(counting).Handle(
            new GetAdminCommunityRestrictionsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(1, counting.AuthorNameLookupCount);
        Assert.Equal(
            new[] { first.Value, second.Value }.OrderBy(id => id),
            counting.LastRequestedAuthorIds.OrderBy(id => id));
        Assert.Contains(
            result.Value.Items,
            item => item.RestrictionId == firstRow.Id.Value
                && item.AuthorArabicName == "الأول"
                && item.AuthorEnglishName == "First");
        Assert.Contains(
            result.Value.Items,
            item => item.RestrictionId == secondRow.Id.Value
                && item.AuthorArabicName == "الثاني"
                && item.AuthorEnglishName == "Second");
    }

    [Fact]
    public async Task Handler_AbsentAuthorIdentityYieldsEmptyNamesNotAFabricatedOne()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        var unknownAuthor = UserId.New();
        await db.AddRestrictionAsync(unknownAuthor, UserId.New(), CommunityRestrictionDuration.Days7);

        var result = await new GetAdminCommunityRestrictionsQueryHandler(db.Context).Handle(
            new GetAdminCommunityRestrictionsQuery(AuthorId: unknownAuthor.Value), default);

        AdminCommunityRestrictionListItem item = Assert.Single(result.Value.Items);
        Assert.Equal(unknownAuthor.Value, item.AuthorId);
        Assert.Equal(string.Empty, item.AuthorArabicName);
        Assert.Equal(string.Empty, item.AuthorEnglishName);
    }

    [Fact]
    public async Task Handler_EmptyReadReturnsNoItemsAndZeroTotal()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();

        var result = await new GetAdminCommunityRestrictionsQueryHandler(db.Context).Handle(
            new GetAdminCommunityRestrictionsQuery(AuthorId: Guid.CreateVersion7(), IsActive: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(25, result.Value.PageSize);
    }

    [Fact]
    public async Task Handler_SharedStartInstantKeepsPagingStableAndComplete()
    {
        await using TestRestrictionDb db = await TestRestrictionDb.CreateAsync();
        UserId sharedStart = UserId.New();
        await db.AddRestrictionAsync(
            sharedStart, UserId.New(), CommunityRestrictionDuration.Days7, startsOnUtc: SeedBaseUtc);
        await db.AddRestrictionAsync(
            sharedStart, UserId.New(), CommunityRestrictionDuration.Days30, startsOnUtc: SeedBaseUtc);
        await db.AddRestrictionAsync(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Hours24, startsOnUtc: SeedBaseUtc);
        await db.AddRestrictionAsync(
            UserId.New(), UserId.New(), CommunityRestrictionDuration.Hours24,
            startsOnUtc: SeedBaseUtc.AddHours(-1));

        var handler = new GetAdminCommunityRestrictionsQueryHandler(db.Context);
        var pageOne = await handler.Handle(new GetAdminCommunityRestrictionsQuery(PageSize: 2), default);
        var pageTwo = await handler.Handle(new GetAdminCommunityRestrictionsQuery(Page: 2, PageSize: 2), default);
        var repeatedPageOne = await handler.Handle(new GetAdminCommunityRestrictionsQuery(PageSize: 2), default);

        Assert.Equal(4, pageOne.Value.TotalCount);
        Assert.Equal(
            pageOne.Value.Items.Select(item => item.RestrictionId),
            repeatedPageOne.Value.Items.Select(item => item.RestrictionId));

        Guid[] paged =
        [
            .. pageOne.Value.Items.Select(item => item.RestrictionId),
            .. pageTwo.Value.Items.Select(item => item.RestrictionId)
        ];
        Assert.Equal(4, paged.Length);
        Assert.Equal(4, paged.Distinct().Count());
        Assert.Equal(await db.Context.CommunityAuthorRestrictions.CountAsync(), paged.Length);
    }

    /// <summary>
    /// SQLite Community context with the same minimal <c>main.users</c> seam the
    /// identity enrichment reads, so author names are resolved by the production
    /// query against the Community context's own connection.
    /// </summary>
    private sealed class TestRestrictionDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestRestrictionDb(SqliteConnection connection, CommunityDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public CommunityDbContext Context { get; }

        public static async Task<TestRestrictionDb> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new CommunityDbContext(
                new DbContextOptionsBuilder<CommunityDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE users (id TEXT NOT NULL PRIMARY KEY, " +
                "arabic_full_name TEXT NOT NULL, english_full_name TEXT NOT NULL, " +
                "avatar_url TEXT NULL);");
            return new TestRestrictionDb(connection, context);
        }

        public Task AddAuthorAsync(UserId authorId, string arabicName, string englishName) =>
            Context.Database.ExecuteSqlRawAsync(
                "INSERT INTO users (id, arabic_full_name, english_full_name) VALUES ({0}, {1}, {2});",
                authorId.Value, arabicName, englishName);

        public async Task<CommunityAuthorRestriction> AddRestrictionAsync(
            UserId authorId,
            UserId moderatorId,
            CommunityRestrictionDuration duration,
            string reasonCode = CommunityRestrictionReasonCodes.Spam,
            string? note = null,
            DateTime? startsOnUtc = null)
        {
            CommunityAuthorRestriction restriction = CommunityAuthorRestriction.Create(
                authorId, moderatorId, duration, reasonCode, note, startsOnUtc ?? DateTime.UtcNow);
            Context.CommunityAuthorRestrictions.Add(restriction);
            await Context.SaveChangesAsync();
            return restriction;
        }

        public async Task<List<CommunityAuthorRestriction>> SeedDescendingStartsAsync(int count)
        {
            var rows = new List<CommunityAuthorRestriction>(count);
            for (int index = 0; index < count; index++)
            {
                rows.Add(await AddRestrictionAsync(
                    UserId.New(), UserId.New(), CommunityRestrictionDuration.Days7,
                    startsOnUtc: SeedBaseUtc.AddHours(-index)));
            }

            return rows;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Delegating <see cref="ICommunityDbContext"/> that records how many times
    /// the identity enrichment is invoked, so an accidental per-row lookup fails
    /// the test instead of passing silently.
    /// </summary>
    private sealed class CountingCommunityDbContext(CommunityDbContext inner) : ICommunityDbContext
    {
        public int AuthorNameLookupCount { get; private set; }

        public IReadOnlyCollection<Guid> LastRequestedAuthorIds { get; private set; } = [];

        public DbSet<Post> Posts => inner.Posts;

        public DbSet<Comment> Comments => inner.Comments;

        public DbSet<Reply> Replies => inner.Replies;

        public DbSet<CheckIn> CheckIns => inner.CheckIns;

        public DbSet<Rating> Ratings => inner.Ratings;

        public DbSet<CommunityInteraction> Interactions => inner.Interactions;

        public DbSet<CommunityImage> CommunityImages => inner.CommunityImages;

        public DbSet<CommunityCategory> Categories => inner.Categories;

        public DbSet<CommunityPostReport> CommunityPostReports => inner.CommunityPostReports;

        public DbSet<CommunityAuthorRestriction> CommunityAuthorRestrictions =>
            inner.CommunityAuthorRestrictions;

        public DbSet<CommunityAuthorBlock> CommunityAuthorBlocks => inner.CommunityAuthorBlocks;

        public Task<IDbContextTransaction?> BeginCommunityAuthorRestrictionTransactionAsync(
            UserId authorId,
            CancellationToken cancellationToken = default) =>
            inner.BeginCommunityAuthorRestrictionTransactionAsync(authorId, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);

        public Task<IReadOnlyDictionary<Guid, CommunityAuthorName>> GetCommunityAuthorNamesAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            AuthorNameLookupCount++;
            LastRequestedAuthorIds = userIds;
            return inner.GetCommunityAuthorNamesAsync(userIds, cancellationToken);
        }

        public Task<IReadOnlyList<Post>> GetRecommendedPostsAsync(
            UserId userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetRecommendedPostsAsync(userId, page, pageSize, cancellationToken);
    }
}
