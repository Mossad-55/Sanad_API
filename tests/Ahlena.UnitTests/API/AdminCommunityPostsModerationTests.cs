using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

/// <summary>
/// FUR-07 admin Community moderation queue/detail contract. Verifies the
/// paged bilingual search and report/status filters, actual persisted counters,
/// author-data minimization, the unchanged moderation policy, and the
/// controller's forced 400/404/409 mapping for the new Community.* codes.
/// </summary>
public sealed class AdminCommunityPostsModerationTests
{
    [Fact]
    public void Controller_RequiresCommunityModerationPolicy()
    {
        var authorize = typeof(AdminCommunityPostsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CommunityModeration, authorize!.Policy);
        Assert.Equal(
            "api/v1/admin/community/posts",
            typeof(AdminCommunityPostsController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Theory]
    [InlineData(nameof(AdminCommunityPostsController.List), null)]
    [InlineData(nameof(AdminCommunityPostsController.Detail), "{postId:guid}")]
    [InlineData(nameof(AdminCommunityPostsController.Publish), "{postId:guid}/publish")]
    [InlineData(nameof(AdminCommunityPostsController.Reject), "{postId:guid}/reject")]
    [InlineData(
        nameof(AdminCommunityPostsController.ResolveReport),
        "/api/v1/admin/community/reports/{reportId:guid}/resolve")]
    [InlineData(
        nameof(AdminCommunityPostsController.CreateRestriction),
        "/api/v1/admin/community/authors/{authorId:guid}/restrictions")]
    [InlineData(
        nameof(AdminCommunityPostsController.RevokeRestriction),
        "/api/v1/admin/community/restrictions/{restrictionId:guid}/revoke")]
    public void Controller_RoutesMatchApprovedContract(string action, string? template)
    {
        HttpMethodAttribute attribute = typeof(AdminCommunityPostsController)
            .GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(template, attribute.Template);
    }

    [Fact]
    public async Task List_ReturnsPageWithActualCounters()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var moderation = await db.SeedModerationScenarioAsync();
        CommunityAuthorRestriction existingRestriction =
            await db.Context.CommunityAuthorRestrictions.SingleAsync();
        db.Context.CommunityAuthorRestrictions.Add(CommunityAuthorRestriction.Create(
            existingRestriction.AuthorId, UserId.New(), CommunityRestrictionDuration.Days7,
            CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow));
        await db.Context.SaveChangesAsync();

        var result = await new GetAdminCommunityPostsQueryHandler(db.Context).Handle(
            new GetAdminCommunityPostsQuery(), default);

        Assert.True(result.IsSuccess);
        AdminCommunityPostsPage page = result.Value;
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal(moderation.PublishedWithDismissedReportId, page.Items[0].Id);
        Assert.Equal(
            new AdminCommunityCounters(1, 2, 1, 1, 1, 1),
            page.Counters);
    }

    [Fact]
    public async Task List_PagesNewestFirstWithStableOrder()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var moderation = await db.SeedModerationScenarioAsync();

        var first = await new GetAdminCommunityPostsQueryHandler(db.Context).Handle(
            new GetAdminCommunityPostsQuery(Page: 1, PageSize: 2), default);
        var second = await new GetAdminCommunityPostsQueryHandler(db.Context).Handle(
            new GetAdminCommunityPostsQuery(Page: 2, PageSize: 2), default);

        Assert.Equal(2, first.Value.Items.Count);
        Assert.Equal(2, second.Value.Items.Count);
        Assert.Equal(moderation.PublishedWithDismissedReportId, first.Value.Items[0].Id);
        Assert.Equal(moderation.RejectedPostId, first.Value.Items[1].Id);
        Assert.Equal(moderation.PublishedWithOpenReportId, second.Value.Items[0].Id);
        Assert.Equal(moderation.PendingPostId, second.Value.Items[1].Id);
        Assert.Empty(first.Value.Items.Select(i => i.Id).Intersect(second.Value.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task List_FiltersByStatusSearchAndReportState()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var moderation = await db.SeedModerationScenarioAsync();
        var handler = new GetAdminCommunityPostsQueryHandler(db.Context);

        var published = await handler.Handle(
            new GetAdminCommunityPostsQuery(Status: "published"), default);
        var rejected = await handler.Handle(
            new GetAdminCommunityPostsQuery(Status: "Rejected"), default);
        var englishSearch = await handler.Handle(
            new GetAdminCommunityPostsQuery(Search: "health"), default);
        var arabicSearch = await handler.Handle(
            new GetAdminCommunityPostsQuery(Search: "نصيحة"), default);
        var openReports = await handler.Handle(
            new GetAdminCommunityPostsQuery(ReportStatus: "Open"), default);
        var dismissedReports = await handler.Handle(
            new GetAdminCommunityPostsQuery(ReportStatus: "dismissed"), default);
        var noReports = await handler.Handle(
            new GetAdminCommunityPostsQuery(ReportStatus: "None"), default);

        Assert.Equal(2, published.Value.TotalCount);
        Assert.Equal(moderation.RejectedPostId, Assert.Single(rejected.Value.Items).Id);
        Assert.Equal(moderation.PublishedWithOpenReportId, Assert.Single(englishSearch.Value.Items).Id);
        Assert.Equal(moderation.PublishedWithOpenReportId, Assert.Single(arabicSearch.Value.Items).Id);
        Assert.Equal(moderation.PublishedWithOpenReportId, Assert.Single(openReports.Value.Items).Id);
        Assert.Equal(moderation.PublishedWithDismissedReportId, Assert.Single(dismissedReports.Value.Items).Id);
        Assert.Equal(2, noReports.Value.TotalCount);
    }

    [Theory]
    [InlineData("not-a-status", null)]
    [InlineData(null, "not-a-report-state")]
    public async Task List_InvalidFilter_ReturnsValidationError(string? status, string? reportStatus)
    {
        await using var db = await TestCommunityDb.CreateAsync();

        var result = await new GetAdminCommunityPostsQueryHandler(db.Context).Handle(
            new GetAdminCommunityPostsQuery(Status: status, ReportStatus: reportStatus), default);

        Assert.True(result.IsFailure);
        Error expected = status is null
            ? CommunityModerationErrors.InvalidReportStatus
            : CommunityModerationErrors.InvalidPostStatus;
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task Detail_ReturnsReportsModerationHistoryAndAuthorNames()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var moderation = await db.SeedModerationScenarioAsync();
        var handler = new GetAdminCommunityPostDetailQueryHandler(db.Context);

        var result = await handler.Handle(
            new GetAdminCommunityPostDetailQuery(moderation.PublishedWithOpenReportId), default);

        Assert.True(result.IsSuccess);
        AdminCommunityPostDetail detail = result.Value;
        Assert.Equal(moderation.AuthorId, detail.AuthorId);
        Assert.Equal("مؤلف", detail.AuthorArabicName);
        Assert.Equal("Author", detail.AuthorEnglishName);
        AdminCommunityPostReportItem report = Assert.Single(detail.Reports);
        Assert.Equal(CommunityReportStatus.Open.ToString(), report.Status);
        Assert.Equal(CommunityReportReasonCodes.Spam, report.ReasonCode);
        AdminCommunityModerationEvent published = Assert.Single(detail.ModerationHistory);
        Assert.Equal("Published", published.Action);
        Assert.Equal(moderation.ModeratorId, published.ActorId);

        var missing = await handler.Handle(
            new GetAdminCommunityPostDetailQuery(Guid.CreateVersion7()), default);
        Assert.True(missing.IsFailure);
        Assert.Equal("Community.PostNotFound", missing.Error.Code);
    }

    [Fact]
    public void AdminPostDtos_NeverExposeAuthorContactData()
    {
        string[] forbidden = ["email", "phone", "contact"];

        foreach (Type dto in new[] { typeof(AdminCommunityPostListItem), typeof(AdminCommunityPostDetail) })
        {
            IEnumerable<string> names = dto.GetProperties().Select(p => p.Name);
            Assert.Contains("AuthorId", names);
            Assert.Contains("AuthorArabicName", names);
            Assert.Contains("AuthorEnglishName", names);
            Assert.DoesNotContain(
                names,
                name => forbidden.Any(token =>
                    name.Contains(token, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task List_InvalidFilter_ReturnsBadRequest()
    {
        var sender = new StubSender
        {
            Response = Result<AdminCommunityPostsPage>.Failure(
                CommunityModerationErrors.InvalidPostStatus)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.List(status: "bogus", cancellationToken: default);

        Assert.Equal(StatusCodes.Status400BadRequest, ProblemStatus(result));
    }

    [Fact]
    public async Task List_Success_ForwardsFiltersAndReturnsPage()
    {
        var expected = new AdminCommunityPostsPage(
            [], 2, 10, 0, new AdminCommunityCounters(0, 0, 0, 0, 0, 0));
        var sender = new StubSender { Response = Result<AdminCommunityPostsPage>.Success(expected) };
        var controller = CreateController(sender);

        IActionResult result = await controller.List(
            status: "Published", reportStatus: "Open", search: "tip",
            page: 2, pageSize: 10, cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        var query = Assert.IsType<GetAdminCommunityPostsQuery>(sender.LastRequest);
        Assert.Equal("Published", query.Status);
        Assert.Equal("Open", query.ReportStatus);
        Assert.Equal("tip", query.Search);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
    }

    [Fact]
    public async Task Reject_ForwardsReasonCodeAndNote()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        var controller = CreateController(sender);
        Guid postId = Guid.CreateVersion7();

        IActionResult result = await controller.Reject(
            postId,
            new RejectCommunityPostRequest(CommunityRejectionReasonCodes.Other, "context"),
            default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<RejectCommunityPostCommand>(sender.LastRequest);
        Assert.Equal(postId, command.PostId);
        Assert.Equal(CommunityRejectionReasonCodes.Other, command.ReasonCode);
        Assert.Equal("context", command.Note);
    }

    [Fact]
    public async Task Reject_InvalidReason_ReturnsBadRequest()
    {
        var sender = new StubSender
        {
            Response = Result<bool>.Failure(CommunityModerationErrors.InvalidRejectionReason)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.Reject(
            Guid.CreateVersion7(), new RejectCommunityPostRequest("Not/Real", null), default);

        Assert.Equal(StatusCodes.Status400BadRequest, ProblemStatus(result));
    }

    [Fact]
    public async Task RejectCommand_PersistsReasonModeratorAndUtcDecision()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var scenario = await db.SeedModerationScenarioAsync();
        var moderator = UserId.New();

        var result = await new RejectCommunityPostCommandHandler(db.Context).Handle(
            new RejectCommunityPostCommand(
                scenario.PendingPostId, moderator, CommunityRejectionReasonCodes.Spam, "  context  "),
            default);

        Assert.True(result.IsSuccess);
        Post rejected = await db.Context.Posts.SingleAsync(
            p => p.Id == new CommunityPostId(scenario.PendingPostId));
        Assert.Equal(PostStatus.Rejected, rejected.Status);
        Assert.Equal(moderator, rejected.RejectedBy);
        Assert.Equal(CommunityRejectionReasonCodes.Spam, rejected.RejectionReasonCode);
        Assert.Equal("context", rejected.RejectionNote);
        Assert.NotNull(rejected.RejectedOnUtc);
        Assert.Equal(DateTimeKind.Utc, rejected.RejectedOnUtc!.Value.Kind);
    }

    [Fact]
    public async Task RejectCommand_OverlongNoteIsValidationFailure()
    {
        await using var db = await TestCommunityDb.CreateAsync();
        var scenario = await db.SeedModerationScenarioAsync();

        var result = await new RejectCommunityPostCommandHandler(db.Context).Handle(
            new RejectCommunityPostCommand(
                scenario.PendingPostId, UserId.New(), CommunityRejectionReasonCodes.Spam,
                new string('x', 1001)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityModerationErrors.InvalidRejectionReason, result.Error);
        Post pending = await db.Context.Posts.SingleAsync(
            p => p.Id == new CommunityPostId(scenario.PendingPostId));
        Assert.Equal(PostStatus.PendingReview, pending.Status);
    }

    [Fact]
    public async Task Detail_UnknownPost_ReturnsNotFound()
    {
        var sender = new StubSender
        {
            Response = Result<AdminCommunityPostDetail>.Failure(
                new Error("Community.PostNotFound", "Community post not found."))
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.Detail(Guid.CreateVersion7(), default);

        Assert.Equal(StatusCodes.Status404NotFound, ProblemStatus(result));
    }

    [Fact]
    public async Task ResolveReport_ForcedAndMappedStatuses()
    {
        Guid reportId = Guid.CreateVersion7();

        Assert.Equal(
            StatusCodes.Status404NotFound,
            ProblemStatus(await InvokeResolveReportAsync(
                reportId, Result<bool>.Failure(CommunityModerationErrors.ReportNotFound))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeResolveReportAsync(
                reportId, Result<bool>.Failure(CommunityModerationErrors.ReportAlreadyResolved))));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            ProblemStatus(await InvokeResolveReportAsync(
                reportId, Result<bool>.Failure(CommunityModerationErrors.InvalidReportResolution))));
    }

    [Fact]
    public async Task Restriction_ForcedAndMappedStatuses()
    {
        var createSender = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionAlreadyActive)
        };
        IActionResult createResult = await CreateController(createSender).CreateRestriction(
            Guid.CreateVersion7(),
            new CreateCommunityAuthorRestrictionRequest(
                nameof(CommunityRestrictionDuration.Hours24),
                CommunityRestrictionReasonCodes.Spam,
                null),
            default);
        Assert.Equal(StatusCodes.Status409Conflict, ProblemStatus(createResult));

        var revokeNotFound = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionNotFound)
        };
        IActionResult revokeNotFoundResult = await CreateController(revokeNotFound).RevokeRestriction(
            Guid.CreateVersion7(), new RevokeCommunityAuthorRestrictionRequest("done"), default);
        Assert.Equal(StatusCodes.Status404NotFound, ProblemStatus(revokeNotFoundResult));

        var revokeAgain = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionAlreadyRevoked)
        };
        IActionResult revokeAgainResult = await CreateController(revokeAgain).RevokeRestriction(
            Guid.CreateVersion7(), new RevokeCommunityAuthorRestrictionRequest("done"), default);
        Assert.Equal(StatusCodes.Status409Conflict, ProblemStatus(revokeAgainResult));
    }

    [Fact]
    public async Task ModerationActions_WithoutSubjectClaim_ReturnUnauthorized()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        AdminCommunityPostsController controller = CreateController(sender, Guid.Empty);

        Assert.IsType<UnauthorizedResult>(
            await controller.Publish(Guid.CreateVersion7(), default));
        Assert.IsType<UnauthorizedResult>(
            await controller.Reject(
                Guid.CreateVersion7(), new RejectCommunityPostRequest("Spam", null), default));
        Assert.Null(sender.LastRequest);
    }

    private static async Task<IActionResult> InvokeResolveReportAsync(Guid reportId, Result<bool> response)
    {
        var sender = new StubSender { Response = response };
        return await CreateController(sender).ResolveReport(
            reportId, new ResolveCommunityPostReportRequest("ActionTaken", null), default);
    }

    private static AdminCommunityPostsController CreateController(StubSender sender, Guid? subject = null)
    {
        Guid value = subject ?? Guid.CreateVersion7();
        return new AdminCommunityPostsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            value == Guid.Empty
                                ? []
                                : [new System.Security.Claims.Claim(
                                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                                    value.ToString("D"))],
                            "Test"))
                }
            }
        };
    }

    private static int? ProblemStatus(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(objectResult.StatusCode, problem.Status);
        return problem.Status;
    }

    private sealed record ModerationScenario(
        Guid PendingPostId,
        Guid PublishedWithOpenReportId,
        Guid PublishedWithDismissedReportId,
        Guid RejectedPostId,
        Guid AuthorId,
        Guid ModeratorId);

    private sealed class TestCommunityDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestCommunityDb(SqliteConnection connection, CommunityDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public CommunityDbContext Context { get; }

        public static async Task<TestCommunityDb> CreateAsync()
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
            return new TestCommunityDb(connection, context);
        }

        public async Task<ModerationScenario> SeedModerationScenarioAsync()
        {
            var author = UserId.New();
            var moderator = UserId.New();
            await Context.Database.ExecuteSqlRawAsync(
                "INSERT INTO users (id, arabic_full_name, english_full_name) VALUES ({0}, {1}, {2});",
                author.Value, "مؤلف", "Author");

            Post pending = Post.Create(
                "معلقة", "Pending", "محتوى", "Content", null, false,
                author, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            Post publishedOpen = PublishedPost(
                author, moderator, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                "نصيحة صحية", "Health tip");
            Post publishedDismissed = PublishedPost(
                author, moderator, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
                "خبر", "Other");
            Post rejected = Post.Create(
                "مرفوضة", "Rejected", "محتوى", "Content", null, false,
                author, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
            rejected.Reject(
                moderator, CommunityRejectionReasonCodes.Spam, null,
                new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc));

            Context.Posts.AddRange(pending, publishedOpen, publishedDismissed, rejected);

            CommunityPostReport openReport = CommunityPostReport.Create(
                publishedOpen.Id, UserId.New(), CommunityReportReasonCodes.Spam, null,
                new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc));
            CommunityPostReport dismissed = CommunityPostReport.Create(
                publishedDismissed.Id, UserId.New(),
                CommunityReportReasonCodes.HarassmentOrAbuse, null,
                new DateTime(2026, 10, 4, 2, 0, 0, DateTimeKind.Utc));
            dismissed.Resolve(
                CommunityReportStatus.Dismissed, moderator, null,
                new DateTime(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc));
            Context.CommunityPostReports.AddRange(openReport, dismissed);

            Context.CommunityAuthorRestrictions.Add(CommunityAuthorRestriction.Create(
                UserId.New(), moderator, CommunityRestrictionDuration.Days7,
                CommunityRestrictionReasonCodes.Spam, null, DateTime.UtcNow));

            await Context.SaveChangesAsync();

            return new ModerationScenario(
                pending.Id.Value,
                publishedOpen.Id.Value,
                publishedDismissed.Id.Value,
                rejected.Id.Value,
                author.Value,
                moderator.Value);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Post PublishedPost(
            UserId author, UserId moderator, DateTime createdOnUtc,
            string titleArabic, string titleEnglish)
        {
            Post post = Post.Create(
                titleArabic, titleEnglish, "محتوى", "Content", null, false, author, createdOnUtc);
            post.Publish(moderator, createdOnUtc.AddMinutes(5));
            return post;
        }
    }

    private sealed class StubSender : ISender
    {
        public object? LastRequest { get; private set; }

        public object? Response { get; init; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)Response!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
