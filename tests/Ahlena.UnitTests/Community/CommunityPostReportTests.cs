using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Community;

/// <summary>
/// FUR-07 report intake and lifecycle. Reports apply to published posts only,
/// a reporter cannot report their own post, and one retained report per
/// reporter/post pair is kept (a repeat returns the existing row). A moderator
/// resolves a report once as ActionTaken or Dismissed; resolution by itself
/// never publishes/rejects content or restricts an author.
/// </summary>
public sealed class CommunityPostReportTests
{
    [Fact]
    public void Create_TrimsNoteAndStartsOpen()
    {
        var report = CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.Spam, "  spammy  ", DateTime.UtcNow);

        Assert.Equal(CommunityReportReasonCodes.Spam, report.ReasonCode);
        Assert.Equal("spammy", report.Note);
        Assert.Equal(CommunityReportStatus.Open, report.Status);
        Assert.Null(report.ResolvedOnUtc);
        Assert.Null(report.ResolvedBy);
        Assert.Equal(0, report.Version);
    }

    [Theory]
    [InlineData("Not/A/Real/Code")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_RejectsUnknownReasonCode(string? reasonCode)
    {
        Assert.Throws<DomainException>(() => CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(), reasonCode, null, DateTime.UtcNow));
    }

    [Fact]
    public void Create_RequiresNoteForOther()
    {
        Assert.Throws<DomainException>(() => CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.Other, "   ", DateTime.UtcNow));

        var report = CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.Other, "details", DateTime.UtcNow);
        Assert.Equal("details", report.Note);
    }

    [Fact]
    public void Create_RejectsEmptyPostOrReporterOrNonUtcTime()
    {
        Assert.Throws<DomainException>(() => CommunityPostReport.Create(
            CommunityPostId.Empty, UserId.New(),
            CommunityReportReasonCodes.Spam, null, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => CommunityPostReport.Create(
            CommunityPostId.New(), UserId.Empty,
            CommunityReportReasonCodes.Spam, null, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.Spam, null,
            new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Resolve_RecordsStatusActorNoteAndVersion()
    {
        var report = CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.HarassmentOrAbuse, null, DateTime.UtcNow);
        var moderator = UserId.New();
        var resolvedOn = DateTime.UtcNow.AddMinutes(1);

        report.Resolve(CommunityReportStatus.ActionTaken, moderator, "  handled  ", resolvedOn);

        Assert.Equal(CommunityReportStatus.ActionTaken, report.Status);
        Assert.Equal(moderator, report.ResolvedBy);
        Assert.Equal(resolvedOn, report.ResolvedOnUtc);
        Assert.Equal("handled", report.ResolutionNote);
        Assert.Equal(1, report.Version);

        Assert.Throws<DomainException>(() => report.Resolve(
            CommunityReportStatus.Dismissed, moderator, null, DateTime.UtcNow));
    }

    [Fact]
    public void Resolve_RejectsInvalidResolution()
    {
        var report = CommunityPostReport.Create(
            CommunityPostId.New(), UserId.New(),
            CommunityReportReasonCodes.Spam, null, DateTime.UtcNow);

        Assert.Throws<DomainException>(() => report.Resolve(
            CommunityReportStatus.Open, UserId.New(), null, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => report.Resolve(
            CommunityReportStatus.ActionTaken, UserId.Empty, null, DateTime.UtcNow));
    }

    [Fact]
    public async Task Submit_PublishedPost_CreatesRetainedReport()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        var reporter = UserId.New();
        Post post = PublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new SubmitCommunityPostReportCommandHandler(db).Handle(
            new SubmitCommunityPostReportCommand(
                post.Id.Value, reporter, CommunityReportReasonCodes.Spam, "spam"),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        CommunityPostReport stored = await db.CommunityPostReports.SingleAsync();
        Assert.Equal(reporter, stored.ReporterId);
        Assert.Equal(post.Id, stored.PostId);
        Assert.Equal(CommunityReportStatus.Open, stored.Status);
    }

    [Fact]
    public async Task Submit_Repeat_ReturnsExistingReportWithoutDuplicate()
    {
        await using var db = CreateDb();
        Post post = PublishedPost(UserId.New());
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var reporter = UserId.New();
        var handler = new SubmitCommunityPostReportCommandHandler(db);

        var first = await handler.Handle(new SubmitCommunityPostReportCommand(
            post.Id.Value, reporter, CommunityReportReasonCodes.Spam, null), default);
        var repeat = await handler.Handle(new SubmitCommunityPostReportCommand(
            post.Id.Value, reporter, CommunityReportReasonCodes.HarassmentOrAbuse, null), default);

        Assert.True(first.IsSuccess);
        Assert.True(first.Value.Created);
        Assert.True(repeat.IsSuccess);
        Assert.False(repeat.Value.Created);
        Assert.Equal(first.Value.ReportId, repeat.Value.ReportId);
        Assert.Equal(1, await db.CommunityPostReports.CountAsync());
    }

    [Fact]
    public async Task Submit_UniqueIndexRaceReturnsTheConcurrentReport()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var competingOptions = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var competingDb = new CommunityDbContext(competingOptions);
        await competingDb.Database.EnsureCreatedAsync();

        Post post = PublishedPost(UserId.New());
        competingDb.Posts.Add(post);
        await competingDb.SaveChangesAsync();
        var reporter = UserId.New();
        var interceptor = new ConcurrentReportInsertInterceptor(
            competingDb, post.Id, reporter);
        var racingOptions = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var racingDb = new CommunityDbContext(racingOptions);

        var result = await new SubmitCommunityPostReportCommandHandler(racingDb).Handle(
            new SubmitCommunityPostReportCommand(
                post.Id.Value, reporter, CommunityReportReasonCodes.Spam, null),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        CommunityPostReport stored = await competingDb.CommunityPostReports.SingleAsync();
        Assert.Equal(stored.Id.Value, result.Value.ReportId);
        Assert.True(interceptor.InsertedCompetingReport);
    }

    [Fact]
    public async Task Submit_NonPublishedPost_ReturnsReportNotAllowed()
    {
        await using var db = CreateDb();
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false,
            UserId.New(), DateTime.UtcNow);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new SubmitCommunityPostReportCommandHandler(db).Handle(
            new SubmitCommunityPostReportCommand(
                post.Id.Value, UserId.New(), CommunityReportReasonCodes.Spam, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.ReportNotAllowed, result.Error);
        Assert.Empty(db.CommunityPostReports);
    }

    [Fact]
    public async Task Submit_OwnPost_ReturnsCannotReportOwnPost()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = PublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var result = await new SubmitCommunityPostReportCommandHandler(db).Handle(
            new SubmitCommunityPostReportCommand(
                post.Id.Value, author, CommunityReportReasonCodes.Spam, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.CannotReportOwnPost, result.Error);
        Assert.Empty(db.CommunityPostReports);
    }

    [Fact]
    public async Task Submit_UnknownPost_ReturnsPostNotFound()
    {
        await using var db = CreateDb();

        var result = await new SubmitCommunityPostReportCommandHandler(db).Handle(
            new SubmitCommunityPostReportCommand(
                Guid.CreateVersion7(), UserId.New(), CommunityReportReasonCodes.Spam, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Community.PostNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Submit_InvalidReasonOrMissingOtherNote_ReturnsInvalidReportReason()
    {
        await using var db = CreateDb();
        Post post = PublishedPost(UserId.New());
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var handler = new SubmitCommunityPostReportCommandHandler(db);

        var unknown = await handler.Handle(new SubmitCommunityPostReportCommand(
            post.Id.Value, UserId.New(), "Not/A/Real/Code", null), default);
        var otherWithoutNote = await handler.Handle(new SubmitCommunityPostReportCommand(
            post.Id.Value, UserId.New(), CommunityReportReasonCodes.Other, "  "), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidReportReason, unknown.Error);
        Assert.True(otherWithoutNote.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidReportReason, otherWithoutNote.Error);
        Assert.Empty(db.CommunityPostReports);
    }

    [Fact]
    public async Task Resolve_OpenReport_RecordsResolution()
    {
        await using var db = CreateDb();
        CommunityPostReport report = OpenReport();
        db.CommunityPostReports.Add(report);
        await db.SaveChangesAsync();
        var moderator = UserId.New();

        var result = await new ResolveCommunityPostReportCommandHandler(db).Handle(
            new ResolveCommunityPostReportCommand(
                report.Id.Value, moderator, "ActionTaken", "handled"),
            default);

        Assert.True(result.IsSuccess);
        CommunityPostReport stored = await db.CommunityPostReports.SingleAsync();
        Assert.Equal(CommunityReportStatus.ActionTaken, stored.Status);
        Assert.Equal(moderator, stored.ResolvedBy);
        Assert.NotNull(stored.ResolvedOnUtc);
        Assert.Equal("handled", stored.ResolutionNote);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Resolve_UnknownReport_ReturnsReportNotFound()
    {
        await using var db = CreateDb();

        var result = await new ResolveCommunityPostReportCommandHandler(db).Handle(
            new ResolveCommunityPostReportCommand(
                Guid.CreateVersion7(), UserId.New(), "Dismissed", null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.ReportNotFound, result.Error);
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_ReturnsReportAlreadyResolved()
    {
        await using var db = CreateDb();
        CommunityPostReport report = OpenReport();
        report.Resolve(CommunityReportStatus.Dismissed, UserId.New(), null, DateTime.UtcNow);
        db.CommunityPostReports.Add(report);
        await db.SaveChangesAsync();

        var result = await new ResolveCommunityPostReportCommandHandler(db).Handle(
            new ResolveCommunityPostReportCommand(
                report.Id.Value, UserId.New(), "ActionTaken", null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.ReportAlreadyResolved, result.Error);
    }

    [Fact]
    public async Task Resolve_OverlongNote_ReturnsValidationErrorAndLeavesReportOpen()
    {
        await using var db = CreateDb();
        CommunityPostReport report = OpenReport();
        db.CommunityPostReports.Add(report);
        await db.SaveChangesAsync();

        var result = await new ResolveCommunityPostReportCommandHandler(db).Handle(
            new ResolveCommunityPostReportCommand(
                report.Id.Value, UserId.New(), "Dismissed", new string('x', 1001)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidReportResolutionNote, result.Error);
        Assert.Equal(CommunityReportStatus.Open, report.Status);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Not/Real")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Resolve_InvalidResolution_ReturnsInvalidReportResolution(string? resolution)
    {
        await using var db = CreateDb();
        CommunityPostReport report = OpenReport();
        db.CommunityPostReports.Add(report);
        await db.SaveChangesAsync();

        var result = await new ResolveCommunityPostReportCommandHandler(db).Handle(
            new ResolveCommunityPostReportCommand(
                report.Id.Value, UserId.New(), resolution, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidReportResolution, result.Error);
        Assert.Equal(CommunityReportStatus.Open, report.Status);
    }

    private static CommunityPostReport OpenReport() => CommunityPostReport.Create(
        CommunityPostId.New(), UserId.New(),
        CommunityReportReasonCodes.MisleadingOrFalseInformation, null, DateTime.UtcNow);

    private static Post PublishedPost(UserId author)
    {
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false,
            author, DateTime.UtcNow);
        post.Publish(UserId.New(), DateTime.UtcNow);
        return post;
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class ConcurrentReportInsertInterceptor(
        CommunityDbContext competingContext,
        CommunityPostId postId,
        UserId reporterId) : SaveChangesInterceptor
    {
        public bool InsertedCompetingReport { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            bool hasPendingReport = eventData.Context?.ChangeTracker
                .Entries<CommunityPostReport>()
                .Any(entry => entry.State == EntityState.Added) == true;

            if (!InsertedCompetingReport && hasPendingReport)
            {
                InsertedCompetingReport = true;
                competingContext.CommunityPostReports.Add(CommunityPostReport.Create(
                    postId, reporterId, CommunityReportReasonCodes.Spam, null, DateTime.UtcNow));
                await competingContext.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
