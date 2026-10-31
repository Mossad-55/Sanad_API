using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Discovery;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// F7 owner facility ratings. Verifies the pinned owner route and policy, that
/// the page and both aggregates exclude hidden ratings, that the reviewer is
/// never identified (there is no field to leak), the None/Open/Resolved flag
/// lifecycle with at-most-one-unresolved-flag-per-rating, the 404/400
/// refusals, and that owners have no hide/restore/keep surface.
/// </summary>
public sealed class OwnerCareHomeRatingTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Controller_RequiresCareHomeOwnerAccessPolicy_OnThePinnedRoute()
    {
        var authorize = typeof(OwnerCareHomeRatingsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, authorize!.Policy);
        Assert.Equal(
            "api/v1/care-homes/facilities/mine/ratings",
            typeof(OwnerCareHomeRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Empty(typeof(OwnerCareHomeRatingsController)
            .GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(OwnerCareHomeRatingsController.List), null, "GET")]
    [InlineData(nameof(OwnerCareHomeRatingsController.Flag), "{ratingId:guid}/flag", "POST")]
    public void Controller_ActionTemplatesAndVerbsMatchThePinnedContract(
        string action, string? template, string verb)
    {
        HttpMethodAttribute attribute = typeof(OwnerCareHomeRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(template, attribute.Template);
        Assert.Equal(verb, Assert.Single(attribute.HttpMethods));
    }

    [Fact]
    public void Controller_PublishesOnlyThePinnedStatusCodes()
    {
        Assert.Equal<int>([200, 400, 404], ProducedStatusCodes(nameof(OwnerCareHomeRatingsController.List)));
        Assert.Equal<int>([204, 400, 404], ProducedStatusCodes(nameof(OwnerCareHomeRatingsController.Flag)));
    }

    [Fact]
    public void Controller_ExposesNoModerationSurfaceToTheOwner()
    {
        string[] actions = typeof(OwnerCareHomeRatingsController)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(OwnerCareHomeRatingsController))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Flag", "List"], actions);
        foreach (string forbidden in new[] { "Hide", "Restore", "Keep", "Resolve", "Delete" })
        {
            Assert.DoesNotContain(forbidden, actions, StringComparer.OrdinalIgnoreCase);
        }

        MethodInfo list = typeof(OwnerCareHomeRatingsController)
            .GetMethod(nameof(OwnerCareHomeRatingsController.List))!;
        Assert.Equal(1, Parameter(list, "page").DefaultValue);
        Assert.Equal(25, Parameter(list, "pageSize").DefaultValue);
    }

    [Fact]
    public async Task OwnerRead_OrdersNewestFirstAndComputesBothAggregatesOverVisibleRatings()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        CareHomeRating oldest = AddRating(db, facility, 5, "Oldest", Now.AddDays(-3));
        CareHomeRating middle = AddRating(db, facility, 4, "Middle", Now.AddDays(-2));
        CareHomeRating newest = AddRating(db, facility, 3, "Newest", Now.AddDays(-1));
        await db.SaveChangesAsync();
        var handler = new GetOwnerCareHomeRatingsQueryHandler(db);

        var all = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);
        var page = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 2), default);
        var second = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 2, 2), default);

        Assert.True(all.IsSuccess);
        Assert.Equal(3, all.Value.TotalCount);
        Assert.Equal(3, all.Value.RatingCount);
        Assert.Equal(4m, all.Value.AverageStars);
        Assert.Equal(1, all.Value.Page);
        Assert.Equal(25, all.Value.PageSize);
        Assert.Equal(
            [newest.Id, middle.Id, oldest.Id],
            all.Value.Items.Select(item => item.RatingId));

        OwnerCareHomeRatingItem oldestProjected = all.Value.Items.Single(item => item.RatingId == oldest.Id);
        Assert.Equal(5, oldestProjected.Stars);
        Assert.Equal("Oldest", oldestProjected.ReviewText);
        Assert.Equal(Now.AddDays(-3), oldestProjected.CreatedOnUtc);
        Assert.Equal(OwnerRatingFlagStatus.None, oldestProjected.FlagStatus);
        Assert.Null(oldestProjected.MyFlagReason);
        Assert.Null(oldestProjected.MyFlaggedOnUtc);

        Assert.Equal([newest.Id, middle.Id], page.Value.Items.Select(item => item.RatingId));
        Assert.Equal(3, page.Value.TotalCount);
        Assert.Equal(2, page.Value.PageSize);
        Assert.Equal(1, page.Value.Page);
        Assert.Equal([oldest.Id], second.Value.Items.Select(item => item.RatingId));
        Assert.Equal(3, second.Value.TotalCount);
        Assert.True(page.Value.Items.Select(item => item.RatingId)
            .Intersect(second.Value.Items.Select(item => item.RatingId)).Count() == 0);
    }

    [Fact]
    public async Task OwnerRead_ExcludesHiddenRatingsFromItemsAndBothAggregates()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        AddRating(db, facility, 5, "Top", Now.AddDays(-2));
        CareHomeRating low = AddRating(db, facility, 1, "Poor", Now.AddDays(-1));
        await db.SaveChangesAsync();
        var handler = new GetOwnerCareHomeRatingsQueryHandler(db);

        var before = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);
        Assert.Equal(2, before.Value.TotalCount);
        Assert.Equal(3m, before.Value.AverageStars);

        Assert.True((await HideAsync(db, low.Id, UserId.New(), "Hidden", Now)).IsSuccess);

        var after = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);
        Assert.Equal(1, after.Value.TotalCount);
        Assert.Equal(1, after.Value.RatingCount);
        Assert.Equal(5m, after.Value.AverageStars);
        Assert.DoesNotContain(after.Value.Items, item => item.RatingId == low.Id);

        Assert.True((await RestoreAsync(db, low.Id, UserId.New(), "Restored", Now.AddHours(1))).IsSuccess);

        var restored = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);
        Assert.Equal(2, restored.Value.TotalCount);
        Assert.Equal(3m, restored.Value.AverageStars);

        // The owner never sees another facility's ratings either.
        CareHomeFacility foreign = await AddApprovedFor(db, UserId.New(), "Foreign home");
        AddRating(db, foreign, 1, "Not mine", Now);
        await db.SaveChangesAsync();
        var unchanged = await handler.Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);
        Assert.Equal(2, unchanged.Value.TotalCount);
        Assert.DoesNotContain(unchanged.Value.Items, item => item.ReviewText == "Not mine");
    }

    [Fact]
    public async Task OwnerItem_PublishesOnlyThePinnedAnonymousFields()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        AddRating(db, facility, 4, "Kind staff", Now);
        await db.SaveChangesAsync();

        var result = await new GetOwnerCareHomeRatingsQueryHandler(db)
            .Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);

        OwnerCareHomeRatingItem item = Assert.Single(result.Value.Items);
        Assert.Equal(4, item.Stars);
        Assert.Equal("Kind staff", item.ReviewText);
        Assert.Equal(Now, item.CreatedOnUtc);
        Assert.Equal(OwnerRatingFlagStatus.None, item.FlagStatus);

        string[] itemProperties = typeof(OwnerCareHomeRatingItem).GetProperties()
            .Select(property => property.Name).ToArray();
        Assert.Equal(
            ["RatingId", "Stars", "ReviewText", "CreatedOnUtc", "FlagStatus", "MyFlagReason", "MyFlaggedOnUtc"],
            itemProperties);
        Assert.DoesNotContain(itemProperties, name => ForbiddenReviewerTokens.Any(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase)));

        string[] pageProperties = typeof(OwnerCareHomeRatingPage).GetProperties()
            .Select(property => property.Name).ToArray();
        Assert.Equal(
            ["Items", "Page", "PageSize", "TotalCount", "AverageStars", "RatingCount"],
            pageProperties);
        Assert.DoesNotContain(pageProperties, name => ForbiddenReviewerTokens.Any(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase)));

        // The reviewer is never identified by the item or by the query.
        Assert.DoesNotContain(
            typeof(OwnerCareHomeRatingItem).GetProperties(),
            property => property.PropertyType == typeof(FamilyId) ||
                property.PropertyType == typeof(UserId) ||
                property.PropertyType == typeof(UserId?));
    }

    [Fact]
    public async Task FlagStatus_MovesFromNoneToOpenToResolvedAndOpensAFreshCaseAfterwards()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        CareHomeRating rating = AddRating(db, facility, 2, "Unfair", Now);
        await db.SaveChangesAsync();
        var handler = new GetOwnerCareHomeRatingsQueryHandler(db);
        var flagHandler = new FlagOwnerCareHomeRatingCommandHandler(db);

        OwnerCareHomeRatingItem none = Assert.Single((await handler.Handle(
            new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default)).Value.Items);
        Assert.Equal(OwnerRatingFlagStatus.None, none.FlagStatus);
        Assert.Null(none.MyFlagReason);
        Assert.Null(none.MyFlaggedOnUtc);

        var opened = await flagHandler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, rating.Id, "  Reviewer was never here  ", Now.AddHours(1)),
            default);
        Assert.True(opened.IsSuccess);

        OwnerCareHomeRatingItem open = Assert.Single((await handler.Handle(
            new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default)).Value.Items);
        Assert.Equal(OwnerRatingFlagStatus.Open, open.FlagStatus);
        Assert.Equal("Reviewer was never here", open.MyFlagReason);
        Assert.Equal(Now.AddHours(1), open.MyFlaggedOnUtc);
        CareHomeRatingFlag firstFlag = Assert.Single(await db.RatingFlags.ToListAsync());
        Assert.True(firstFlag.IsOpen);
        Assert.Equal(owner, firstFlag.OwnerUserId);

        // An admin resolution (keep) closes the owner's case.
        Assert.True((await KeepAsync(db, rating.Id, UserId.New(), "Review is accurate", Now.AddHours(2))).IsSuccess);

        OwnerCareHomeRatingItem resolved = Assert.Single((await handler.Handle(
            new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default)).Value.Items);
        Assert.Equal(OwnerRatingFlagStatus.Resolved, resolved.FlagStatus);
        Assert.Equal("Reviewer was never here", resolved.MyFlagReason);
        Assert.Equal(Now.AddHours(1), resolved.MyFlaggedOnUtc);

        // A flag raised after resolution opens a fresh case, not a silent no-op.
        var reopened = await flagHandler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, rating.Id, "Still unfair", Now.AddHours(3)),
            default);
        Assert.True(reopened.IsSuccess);

        OwnerCareHomeRatingItem openAgain = Assert.Single((await handler.Handle(
            new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default)).Value.Items);
        Assert.Equal(OwnerRatingFlagStatus.Open, openAgain.FlagStatus);
        Assert.Equal("Still unfair", openAgain.MyFlagReason);
        Assert.Equal(Now.AddHours(3), openAgain.MyFlaggedOnUtc);

        List<CareHomeRatingFlag> flags = await db.RatingFlags
            .OrderBy(flag => flag.CreatedOnUtc).ToListAsync();
        Assert.Equal(2, flags.Count);
        Assert.False(flags[0].IsOpen);
        Assert.Equal(CareHomeRatingFlagResolution.Kept, flags[0].Resolution);
        Assert.True(flags[1].IsOpen);
    }

    [Fact]
    public async Task Flag_RepeatWhileOpen_ReturnsSuccessAndWritesNothingExtra()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        CareHomeRating rating = AddRating(db, facility, 2, "Unfair", Now);
        await db.SaveChangesAsync();
        var handler = new FlagOwnerCareHomeRatingCommandHandler(db);

        var first = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, rating.Id, "First reason", Now.AddHours(1)), default);
        var second = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, rating.Id, "Second reason", Now.AddHours(2)), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        CareHomeRatingFlag flag = Assert.Single(await db.RatingFlags.ToListAsync());
        Assert.Equal("First reason", flag.Reason);
        Assert.Equal(Now.AddHours(1), flag.CreatedOnUtc);
        Assert.True(flag.IsOpen);
    }

    [Fact]
    public async Task Flag_UnknownOrNotOwnedRating_ReturnsNotFound()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        AddRating(db, facility, 2, "Mine", Now);
        CareHomeFacility foreign = await AddApprovedFor(db, UserId.New(), "Foreign home");
        CareHomeRating foreignRating = AddRating(db, foreign, 2, "Not mine", Now);
        await db.SaveChangesAsync();
        var handler = new FlagOwnerCareHomeRatingCommandHandler(db);

        var unknown = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, Guid.NewGuid(), "Reason", Now), default);
        var empty = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, Guid.Empty, "Reason", Now), default);
        var notMine = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(owner, foreignRating.Id, "Reason", Now), default);
        var noFacility = await handler.Handle(
            new FlagOwnerCareHomeRatingCommand(UserId.New(), Guid.NewGuid(), "Reason", Now), default);

        Assert.Equal("CareHomes.Rating.NotFound", unknown.Error.Code);
        Assert.Equal("CareHomes.Rating.NotFound", empty.Error.Code);
        Assert.Equal("CareHomes.Rating.NotFound", notMine.Error.Code);
        Assert.Equal("CareHomes.Rating.NotFound", noFacility.Error.Code);
        Assert.Empty(await db.RatingFlags.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Flag_BlankReason_IsRejectedWithoutAFlagRow(string? reason)
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        CareHomeRating rating = AddRating(db, facility, 2, "Unfair", Now);
        await db.SaveChangesAsync();

        var result = await new FlagOwnerCareHomeRatingCommandHandler(db).Handle(
            new FlagOwnerCareHomeRatingCommand(owner, rating.Id, reason, Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, result.Error);
        Assert.Empty(await db.RatingFlags.ToListAsync());
    }

    [Fact]
    public async Task Flag_ReasonOverTheBound_IsRejectedAndOneAtTheBoundIsAccepted()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddApprovedFor(db, owner, "Owned home");
        CareHomeRating first = AddRating(db, facility, 2, "Unfair", Now);
        CareHomeRating second = AddRating(db, facility, 2, "Also unfair", Now.AddMinutes(1));
        await db.SaveChangesAsync();
        var handler = new FlagOwnerCareHomeRatingCommandHandler(db);

        var tooLong = await handler.Handle(new FlagOwnerCareHomeRatingCommand(
            owner, first.Id, new string('x', CareHomeRatingFlag.MaximumReasonLength + 1), Now), default);
        var atBound = await handler.Handle(new FlagOwnerCareHomeRatingCommand(
            owner, second.Id, new string('y', CareHomeRatingFlag.MaximumReasonLength), Now), default);

        Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, tooLong.Error);
        Assert.True(atBound.IsSuccess);
        CareHomeRatingFlag stored = Assert.Single(await db.RatingFlags.ToListAsync());
        Assert.Equal(CareHomeRatingFlag.MaximumReasonLength, stored.Reason.Length);
    }

    [Fact]
    public async Task OwnerRead_NoOwnedFacility_ReturnsFacilityNotFound()
    {
        await using CareHomesDbContext db = CreateDb();
        await AddApprovedFor(db, UserId.New(), "Someone else's home");

        var result = await new GetOwnerCareHomeRatingsQueryHandler(db)
            .Handle(new GetOwnerCareHomeRatingsQuery(UserId.New(), 1, 25), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Facility.NotFound", result.Error.Code);
        Assert.Equal(404, MapperStatus(result.Error));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task OwnerRead_InvalidPaging_ReturnsTheInvalidQueryCode(int page, int pageSize)
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        await AddApprovedFor(db, owner, "Owned home");

        var result = await new GetOwnerCareHomeRatingsQueryHandler(db)
            .Handle(new GetOwnerCareHomeRatingsQuery(owner, page, pageSize), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.InvalidQuery, result.Error);
    }

    [Fact]
    public async Task OwnerRead_EmptyFacility_ReturnsZeroedAggregatesAndNoItems()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        await AddApprovedFor(db, owner, "Unrated home");

        var result = await new GetOwnerCareHomeRatingsQueryHandler(db)
            .Handle(new GetOwnerCareHomeRatingsQuery(owner, 1, 25), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(0, result.Value.RatingCount);
        Assert.Equal(0m, result.Value.AverageStars);
    }

    [Fact]
    public async Task Controller_Flag_ForwardsTheCommandAndReturnsNoContent()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        Guid ratingId = Guid.CreateVersion7();
        Guid actorUserId = Guid.CreateVersion7();

        IActionResult result = await CreateController(sender, actorUserId)
            .Flag(ratingId, new FlagCareHomeRatingRequest("Reason"), default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<FlagOwnerCareHomeRatingCommand>(sender.LastRequest);
        Assert.Equal(ratingId, command.RatingId);
        Assert.Equal(new UserId(actorUserId), command.ActorUserId);
        Assert.Equal("Reason", command.Reason);
    }

    [Fact]
    public async Task Controller_Flag_FailureStatusesMatchThePinnedMapping()
    {
        Guid ratingId = Guid.CreateVersion7();

        Assert.Equal(
            StatusCodes.Status404NotFound,
            ProblemStatus(await InvokeFlagAsync(
                ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.NotFound))));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            ProblemStatus(await InvokeFlagAsync(
                ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason))));
    }

    [Fact]
    public async Task Controller_List_ForwardsTheQueryAndReturnsThePayload()
    {
        var page = new OwnerCareHomeRatingPage(
            [new OwnerCareHomeRatingItem(Guid.CreateVersion7(), 4, "Kind", Now, OwnerRatingFlagStatus.None, null, null)],
            1, 25, 1, 4m, 1);
        var sender = new StubSender { Response = Result<OwnerCareHomeRatingPage>.Success(page) };
        Guid actorUserId = Guid.CreateVersion7();

        IActionResult result = await CreateController(sender, actorUserId).List(1, 25, default);

        Assert.Same(page, Assert.IsType<OkObjectResult>(result).Value);
        var query = Assert.IsType<GetOwnerCareHomeRatingsQuery>(sender.LastRequest);
        Assert.Equal(new UserId(actorUserId), query.ActorUserId);
        Assert.Equal(1, query.Page);
        Assert.Equal(25, query.PageSize);
    }

    [Fact]
    public async Task Controller_WithoutTheSubjectClaim_ReturnsUnauthorized()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        OwnerCareHomeRatingsController controller = CreateController(sender, Guid.Empty);

        Assert.IsType<UnauthorizedResult>(await controller.List(1, 25, default));
        Assert.IsType<UnauthorizedResult>(
            await controller.Flag(Guid.CreateVersion7(), new FlagCareHomeRatingRequest("Reason"), default));
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public void Mapper_PinsTheOwnerReadAndFlagStatusCodes()
    {
        Assert.Equal(404, MapperStatus(CareHomeRatingModerationErrors.NotFound));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidReason));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidQuery));
        Assert.Equal(404, MapperStatus(new Error("CareHomes.Facility.NotFound", "Care-home facility was not found.")));
    }

    private static readonly string[] ForbiddenReviewerTokens =
        ["family", "userid", "email", "phone", "avatar", "reviewer", "displayname", "createdby", "owneruserid"];

    private static Task<Result<bool>> HideAsync(
        ICareHomesDbContext db, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        new HideAdminCareHomeRatingCommandHandler(db)
            .Handle(new HideAdminCareHomeRatingCommand(ratingId, actor, reason, utcNow), default);

    private static Task<Result<bool>> KeepAsync(
        ICareHomesDbContext db, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        new KeepAdminCareHomeRatingCommandHandler(db)
            .Handle(new KeepAdminCareHomeRatingCommand(ratingId, actor, reason, utcNow), default);

    private static Task<Result<bool>> RestoreAsync(
        ICareHomesDbContext db, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        new RestoreAdminCareHomeRatingCommandHandler(db)
            .Handle(new RestoreAdminCareHomeRatingCommand(ratingId, actor, reason, utcNow), default);

    private static Task<IActionResult> InvokeFlagAsync(Guid ratingId, Result<bool> response) =>
        CreateController(new StubSender { Response = response }, Guid.CreateVersion7())
            .Flag(ratingId, new FlagCareHomeRatingRequest("Reason"), default);

    private static CareHomeRating AddRating(
        CareHomesDbContext db, CareHomeFacility facility, int stars, string? reviewText, DateTime createdOnUtc)
    {
        CareHomeRating rating = CareHomeRating.Create(
            Guid.NewGuid(), facility.Id, FamilyId.New(), UserId.New(), stars, reviewText, createdOnUtc);
        db.Ratings.Add(rating);
        return rating;
    }

    private static async Task<CareHomeFacility> AddApprovedFor(
        CareHomesDbContext db, UserId owner, string name)
    {
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft() with { EnglishName = name }, Now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{name}-{type}.pdf", "application/pdf", 10, null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        TimeZoneInfo cairo;
        try { cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, cairo)));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static CareHomesDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static int[] ProducedStatusCodes(string action) =>
        [.. typeof(OwnerCareHomeRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)];

    private static ParameterInfo Parameter(MethodInfo method, string name) =>
        method.GetParameters().Single(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

    private static int MapperStatus(Error error) =>
        ResultProblemDetailsMapper.Create(error, new DefaultHttpContext()).Status ?? -1;

    private static OwnerCareHomeRatingsController CreateController(StubSender sender, Guid subjectUserId) =>
        new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            subjectUserId == Guid.Empty
                                ? []
                                : [new System.Security.Claims.Claim(
                                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                                    subjectUserId.ToString("D"))],
                            "Test"))
                }
            }
        };

    private static int ProblemStatus(IActionResult result) =>
        result switch
        {
            NoContentResult => StatusCodes.Status204NoContent,
            ObjectResult objectResult => objectResult.StatusCode ?? -1,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => -1
        };

    private sealed class StubSender : ISender
    {
        public object? LastRequest { get; private set; }

        public object? Response { get; init; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
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
