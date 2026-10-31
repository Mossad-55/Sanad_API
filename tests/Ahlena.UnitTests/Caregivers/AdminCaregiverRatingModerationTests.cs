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
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Application.Discovery;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Application.Landing;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Caregivers;

/// <summary>
/// F7 caregiver-rating visibility and moderation. Verifies the reused
/// caregiver-review admin policy and pinned route contract, the pinned
/// 404/400/409 precedence, that hiding and restoring recompute the stored
/// caregiver aggregate over the non-hidden set (the single source of truth
/// every stored-aggregate consumer reads), that moderation never mutates the
/// retained rating content, that a family re-rating cannot unhide, that the
/// admin projections expose no reviewer identity, and that the landing
/// testimonial resolver drops a hidden rating without treating it as dangling.
/// </summary>
public sealed class AdminCaregiverRatingModerationTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Controller_RequiresCaregiverReviewAdminPolicy_OnThePinnedRoute()
    {
        var authorize = typeof(AdminCaregiverRatingsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CaregiverReviewAdmin, authorize!.Policy);
        Assert.Equal(
            "api/v1/admin/caregivers/ratings",
            typeof(AdminCaregiverRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Empty(typeof(AdminCaregiverRatingsController)
            .GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(AdminCaregiverRatingsController.List), null, "GET")]
    [InlineData(nameof(AdminCaregiverRatingsController.Hide), "{ratingId:guid}/hide", "POST")]
    [InlineData(nameof(AdminCaregiverRatingsController.Restore), "{ratingId:guid}/restore", "POST")]
    public void Controller_ActionTemplatesAndVerbsMatchThePinnedContract(
        string action, string? template, string verb)
    {
        HttpMethodAttribute attribute = typeof(AdminCaregiverRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(template, attribute.Template);
        Assert.Equal(verb, Assert.Single(attribute.HttpMethods));
    }

    [Fact]
    public void Controller_PublishesOnlyThePinnedStatusCodes()
    {
        Assert.Equal<int>([200, 400], ProducedStatusCodes(nameof(AdminCaregiverRatingsController.List)));
        Assert.Equal<int>([204, 400, 404, 409], ProducedStatusCodes(nameof(AdminCaregiverRatingsController.Hide)));
        Assert.Equal<int>([204, 400, 404, 409], ProducedStatusCodes(nameof(AdminCaregiverRatingsController.Restore)));
    }

    [Fact]
    public async Task Hide_AndRestore_RecomputeTheStoredAggregateOverTheNonHiddenSet()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId family = UserId.New();
        Guid bookingTop = Guid.NewGuid();
        Guid bookingMid = Guid.NewGuid();
        Guid bookingLow = Guid.NewGuid();

        // The family write path is the real producer of the stored aggregate.
        await RateAsync(db, caregiver, family, bookingTop, 5, "Excellent", Now);
        await RateAsync(db, caregiver, family, bookingMid, 4, "Good", Now.AddMinutes(1));
        await RateAsync(db, caregiver, family, bookingLow, 3, "Fine", Now.AddMinutes(2));
        Assert.Equal(4m, caregiver.AverageRating);
        Assert.Equal(3, caregiver.ReviewsCount);

        Guid lowId = await RatingIdAsync(db, bookingLow);
        Guid midId = await RatingIdAsync(db, bookingMid);
        UserId admin = UserId.New();

        Assert.True((await HideAsync(db, lowId, admin, "Off topic", Now.AddHours(1))).IsSuccess);
        Assert.Equal(4.5m, caregiver.AverageRating);
        Assert.Equal(2, caregiver.ReviewsCount);

        CaregiverRating hidden = await db.CaregiverRatings.SingleAsync(rating => rating.Id == lowId);
        Assert.True(hidden.IsHidden);
        Assert.Equal(3, hidden.Stars);
        Assert.Equal("Fine", hidden.ReviewText);
        Assert.Equal<UserId?>(admin, hidden.HiddenByUserId);
        Assert.Equal(Now.AddHours(1), hidden.HiddenOnUtc);
        Assert.Equal("Off topic", hidden.HideReason);

        Assert.True((await HideAsync(db, midId, admin, "Duplicate", Now.AddHours(2))).IsSuccess);
        Assert.Equal(5m, caregiver.AverageRating);
        Assert.Equal(1, caregiver.ReviewsCount);

        Assert.True((await RestoreAsync(db, midId, admin, "Appeal upheld", Now.AddHours(3))).IsSuccess);
        Assert.Equal(4.5m, caregiver.AverageRating);
        Assert.Equal(2, caregiver.ReviewsCount);

        CaregiverRating restored = await db.CaregiverRatings.SingleAsync(rating => rating.Id == midId);
        Assert.False(restored.IsHidden);
        Assert.Null(restored.HiddenByUserId);
        Assert.Null(restored.HiddenOnUtc);
        Assert.Null(restored.HideReason);
        Assert.Equal(4, restored.Stars);
        Assert.Equal("Good", restored.ReviewText);

        Assert.True((await RestoreAsync(db, lowId, admin, "Appeal upheld", Now.AddHours(4))).IsSuccess);
        Assert.Equal(4m, caregiver.AverageRating);
        Assert.Equal(3, caregiver.ReviewsCount);

        List<CaregiverRatingModerationAction> actions = await db.RatingModerationActions
            .OrderBy(action => action.OccurredOnUtc)
            .ToListAsync();
        Assert.Equal(4, actions.Count);
        Assert.Equal(
            [
                CaregiverRatingModerationActionType.Hidden,
                CaregiverRatingModerationActionType.Hidden,
                CaregiverRatingModerationActionType.Restored,
                CaregiverRatingModerationActionType.Restored
            ],
            actions.Select(action => action.Action));
        Assert.All(actions, action => Assert.Equal(admin, action.ActorUserId));
        Assert.All(actions, action => Assert.Equal(caregiver.Id, action.CaregiverId));
        Assert.Equal("Off topic", actions[0].Reason);
        Assert.Equal("Duplicate", actions[1].Reason);
    }

    [Fact]
    public async Task Hide_AVisibleRating_SetsTheVisibilityFactsAndAppendsOneAction()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();

        var result = await HideAsync(db, rating.Id, admin, "  Untrue claims  ", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        CaregiverRating stored = await db.CaregiverRatings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(RatingModerationState.Hidden, stored.ModerationState);
        Assert.Equal("Untrue claims", stored.HideReason);
        CaregiverRatingModerationAction action = Assert.Single(await db.RatingModerationActions.ToListAsync());
        Assert.Equal(CaregiverRatingModerationActionType.Hidden, action.Action);
        Assert.Equal(admin, action.ActorUserId);
        Assert.Equal(Now.AddHours(1), action.OccurredOnUtc);
    }

    [Fact]
    public async Task Hide_WhenAlreadyHidden_IsAConflictAndWritesNothing()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();
        Assert.True((await HideAsync(db, rating.Id, admin, "First", Now.AddHours(1))).IsSuccess);

        var second = await HideAsync(db, rating.Id, admin, "Second", Now.AddHours(2));

        Assert.False(second.IsSuccess);
        Assert.Equal(CaregiverRatingModerationErrors.AlreadyHidden, second.Error);
        Assert.Equal(1, await db.RatingModerationActions.CountAsync());
        CaregiverRating stored = await db.CaregiverRatings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal("First", stored.HideReason);
        Assert.Equal(Now.AddHours(1), stored.HiddenOnUtc);
    }

    [Fact]
    public async Task Restore_WhenVisible_IsAConflictAndWritesNothing()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);

        var result = await RestoreAsync(db, rating.Id, UserId.New(), "Nothing to restore", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(CaregiverRatingModerationErrors.AlreadyVisible, result.Error);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hide_AndRestore_UnknownRating_ReturnNotFound(bool hide)
    {
        using CaregiversDbContext db = CreateDb();

        var missing = hide
            ? await HideAsync(db, Guid.NewGuid(), UserId.New(), "Reason", Now)
            : await RestoreAsync(db, Guid.NewGuid(), UserId.New(), "Reason", Now);
        var empty = hide
            ? await HideAsync(db, Guid.Empty, UserId.New(), "Reason", Now)
            : await RestoreAsync(db, Guid.Empty, UserId.New(), "Reason", Now);

        Assert.Equal(CaregiverRatingModerationErrors.NotFound, missing.Error);
        Assert.Equal(CaregiverRatingModerationErrors.NotFound, empty.Error);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
    }

    [Fact]
    public async Task Hide_InvalidReason_IsRejectedBeforeTheStateAndChangesNothing()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();

        foreach (string? reason in new string?[]
                 {
                     null, string.Empty, "   ", new string('x', CaregiverRating.MaximumModerationReasonLength + 1)
                 })
        {
            var result = await HideAsync(db, rating.Id, admin, reason, Now.AddHours(1));
            Assert.False(result.IsSuccess);
            Assert.Equal(CaregiverRatingModerationErrors.InvalidReason, result.Error);
        }

        CaregiverRating stored = await db.CaregiverRatings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(RatingModerationState.Visible, stored.ModerationState);
        Assert.Null(stored.HideReason);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
    }

    [Fact]
    public async Task Hide_BlankReasonOnAHiddenRating_PrefersTheReasonCodeOverTheStateCode()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();
        Assert.True((await HideAsync(db, rating.Id, admin, "First", Now.AddHours(1))).IsSuccess);

        var result = await HideAsync(db, rating.Id, admin, "  ", Now.AddHours(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(CaregiverRatingModerationErrors.InvalidReason, result.Error);
    }

    [Fact]
    public async Task Hide_ReasonOfExactlyTheBound_IsAccepted()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 4);
        string reason = new('r', CaregiverRating.MaximumModerationReasonLength);

        var result = await HideAsync(db, rating.Id, UserId.New(), reason, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        CaregiverRating stored = await db.CaregiverRatings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(reason, stored.HideReason);
    }

    [Fact]
    public async Task FamilyEdit_CannotUnhideARatingAndStaysOutOfTheAggregate()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId family = UserId.New();
        Guid bookingId = Guid.NewGuid();
        await RateAsync(db, caregiver, family, bookingId, 5, "Great", Now);
        Guid ratingId = await RatingIdAsync(db, bookingId);
        Assert.True((await HideAsync(db, ratingId, UserId.New(), "Hidden", Now.AddHours(1))).IsSuccess);
        Assert.Equal(0m, caregiver.AverageRating);
        Assert.Equal(0, caregiver.ReviewsCount);

        var edit = await RateAsync(db, caregiver, family, bookingId, 1, "Changed my mind", Now.AddHours(2));

        Assert.True(edit.IsSuccess);
        CaregiverRating stored = await db.CaregiverRatings.SingleAsync(item => item.Id == ratingId);
        Assert.True(stored.IsHidden);
        Assert.Equal(1, stored.Stars);
        Assert.Equal("Changed my mind", stored.ReviewText);
        Assert.Equal(0m, caregiver.AverageRating);
        Assert.Equal(0, caregiver.ReviewsCount);
        Assert.Single(await db.RatingModerationActions.ToListAsync());
    }

    [Fact]
    public async Task HidingTheOnlyRating_ResetsTheStoredAggregateToZero()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        Guid bookingId = Guid.NewGuid();
        await RateAsync(db, caregiver, UserId.New(), bookingId, 2, null, Now);
        Assert.Equal(2m, caregiver.AverageRating);

        var result = await HideAsync(db, await RatingIdAsync(db, bookingId), UserId.New(), "Hidden", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, caregiver.AverageRating);
        Assert.Equal(0, caregiver.ReviewsCount);
    }

    [Fact]
    public async Task AdminList_FiltersOrdersAndPagesOverTheRetainedContent()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver first = await AddCaregiverAsync(db);
        Caregiver second = await AddCaregiverAsync(db);
        CaregiverRating oldest = AddRating(db, first, 5, "Oldest", Now.AddDays(-3));
        CaregiverRating middle = AddRating(db, first, 4, "Middle", Now.AddDays(-2));
        CaregiverRating newest = AddRating(db, first, 4, "Newest", Now.AddDays(-1));
        CaregiverRating other = AddRating(db, second, 2, "Other caregiver", Now);
        await db.SaveChangesAsync();
        Assert.True((await HideAsync(db, middle.Id, UserId.New(), "Hidden", Now)).IsSuccess);
        var handler = new GetAdminCaregiverRatingsQueryHandler(db);

        var all = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, null, null, 1, 25), default);
        var hidden = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, null, "hidden", 1, 25), default);
        var visible = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, null, "visible", 1, 25), default);
        var byStars = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, 4, "all", 1, 25), default);
        var byCaregiver = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(second.Id.Value, null, null, 1, 25), default);
        var firstPage = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, null, "all", 1, 2), default);
        var secondPage = await handler.Handle(
            new GetAdminCaregiverRatingsQuery(null, null, "all", 2, 2), default);

        Assert.Equal(4, all.Value.TotalCount);
        Assert.Equal([other.Id, newest.Id, middle.Id, oldest.Id], all.Value.Items.Select(item => item.RatingId));
        Assert.Equal(25, all.Value.PageSize);
        Assert.Equal("hidden", Assert.Single(hidden.Value.Items).ModerationState);
        Assert.Equal(middle.Id, Assert.Single(hidden.Value.Items).RatingId);
        Assert.Equal(1, hidden.Value.TotalCount);
        Assert.Equal(3, visible.Value.TotalCount);
        Assert.Equal([newest.Id, middle.Id], byStars.Value.Items.Select(item => item.RatingId));
        Assert.Equal(other.Id, Assert.Single(byCaregiver.Value.Items).RatingId);
        Assert.Equal([other.Id, newest.Id], firstPage.Value.Items.Select(item => item.RatingId));
        Assert.Equal([middle.Id, oldest.Id], secondPage.Value.Items.Select(item => item.RatingId));
        Assert.Equal(4, firstPage.Value.TotalCount);
        Assert.Equal(4, secondPage.Value.TotalCount);
        Assert.True(firstPage.Value.Items.Select(item => item.RatingId)
            .Intersect(secondPage.Value.Items.Select(item => item.RatingId)).Count() == 0);
    }

    [Fact]
    public async Task AdminList_HiddenItemCarriesTheAttributionAndNoReviewerIdentity()
    {
        using CaregiversDbContext db = CreateDb();
        CaregiverRating rating = await AddRatingAsync(db, stars: 3);
        UserId admin = UserId.New();
        Assert.True((await HideAsync(db, rating.Id, admin, "Abusive wording", Now.AddHours(1))).IsSuccess);

        var result = await new GetAdminCaregiverRatingsQueryHandler(db).Handle(
            new GetAdminCaregiverRatingsQuery(null, null, "hidden", 1, 25), default);

        AdminCaregiverRatingListItem item = Assert.Single(result.Value.Items);
        Assert.Equal(rating.Id, item.RatingId);
        Assert.Equal(rating.CaregiverId.Value, item.CaregiverId);
        Assert.Equal(rating.BookingId, item.BookingId);
        Assert.Equal(rating.FamilyId.Value, item.FamilyId);
        Assert.Equal(3, item.Stars);
        Assert.Equal("hidden", item.ModerationState);
        Assert.Equal<Guid?>(admin.Value, item.HiddenByUserId);
        Assert.Equal(Now.AddHours(1), item.HiddenOnUtc);
        Assert.Equal("Abusive wording", item.HideReason);
        Assert.Equal(rating.CreatedOnUtc, item.CreatedOnUtc);
        Assert.Equal(rating.UpdatedOnUtc, item.UpdatedOnUtc);

        string[] published = typeof(AdminCaregiverRatingListItem).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            [
                "RatingId", "CaregiverId", "BookingId", "FamilyId", "Stars", "ReviewText",
                "CreatedOnUtc", "UpdatedOnUtc", "ModerationState", "HiddenByUserId", "HiddenOnUtc", "HideReason"
            ],
            published);
        Assert.DoesNotContain(published, name => ForbiddenReviewerTokens.Any(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(0, 25, null, null)]
    [InlineData(1, 0, null, null)]
    [InlineData(1, 101, null, null)]
    [InlineData(1, 25, 0, null)]
    [InlineData(1, 25, 6, null)]
    [InlineData(1, 25, null, "sometimes")]
    public async Task AdminList_InvalidFilters_ReturnTheInvalidQueryCode(
        int page, int pageSize, int? stars, string? moderation)
    {
        using CaregiversDbContext db = CreateDb();

        var result = await new GetAdminCaregiverRatingsQueryHandler(db).Handle(
            new GetAdminCaregiverRatingsQuery(null, stars, moderation, page, pageSize), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CaregiverRatingModerationErrors.InvalidQuery, result.Error);
    }

    [Fact]
    public async Task Controller_Hide_ForwardsTheCommandAndReturnsNoContent()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        Guid ratingId = Guid.CreateVersion7();
        Guid actorUserId = Guid.CreateVersion7();

        IActionResult result = await CreateController(sender, actorUserId)
            .Hide(ratingId, new ModerateCaregiverRatingRequest("Reason"), default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<HideAdminCaregiverRatingCommand>(sender.LastRequest);
        Assert.Equal(ratingId, command.RatingId);
        Assert.Equal(new UserId(actorUserId), command.ActorUserId);
        Assert.Equal("Reason", command.Reason);
    }

    [Fact]
    public async Task Controller_FailureStatusesMatchThePinnedMapping()
    {
        Guid ratingId = Guid.CreateVersion7();

        Assert.Equal(
            StatusCodes.Status404NotFound,
            ProblemStatus(await InvokeHideAsync(
                ratingId, Result<bool>.Failure(CaregiverRatingModerationErrors.NotFound))));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            ProblemStatus(await InvokeHideAsync(
                ratingId, Result<bool>.Failure(CaregiverRatingModerationErrors.InvalidReason))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeHideAsync(
                ratingId, Result<bool>.Failure(CaregiverRatingModerationErrors.AlreadyHidden))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeRestoreAsync(
                ratingId, Result<bool>.Failure(CaregiverRatingModerationErrors.AlreadyVisible))));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            ProblemStatus(await InvokeRestoreAsync(
                ratingId, Result<bool>.Failure(CaregiverRatingModerationErrors.InvalidQuery))));
    }

    [Fact]
    public async Task Controller_ModerationWithoutTheSubjectClaim_ReturnsUnauthorized()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        AdminCaregiverRatingsController controller = CreateController(sender, Guid.Empty);

        Assert.IsType<UnauthorizedResult>(
            await controller.Hide(Guid.CreateVersion7(), new ModerateCaregiverRatingRequest("Reason"), default));
        Assert.IsType<UnauthorizedResult>(
            await controller.Restore(Guid.CreateVersion7(), new ModerateCaregiverRatingRequest("Reason"), default));
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public void Mapper_PinsTheModerationStatusCodes()
    {
        Assert.Equal(404, MapperStatus(CaregiverRatingModerationErrors.NotFound));
        Assert.Equal(400, MapperStatus(CaregiverRatingModerationErrors.InvalidReason));
        Assert.Equal(400, MapperStatus(CaregiverRatingModerationErrors.InvalidQuery));
        Assert.Equal(409, MapperStatus(CaregiverRatingModerationErrors.AlreadyHidden));
        Assert.Equal(409, MapperStatus(CaregiverRatingModerationErrors.AlreadyVisible));
    }

    [Fact]
    public async Task HiddenRating_IsSkippedByLandingTestimonials_WithoutCountingAsDangling()
    {
        await using CmsDbContext cmsDb = CreateCmsDb();
        await using FamiliesDbContext familiesDb = CreateFamiliesDb();
        await using CaregiversDbContext caregiversDb = CreateDb();

        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        caregiversDb.Caregivers.Add(caregiver);

        CaregiverRating visible = AddRating(caregiversDb, caregiver, 5, "Retained public testimonial", Now);
        CaregiverRating hidden = AddRating(caregiversDb, caregiver, 1, "Moderated testimonial", Now.AddMinutes(1));
        hidden.Hide(UserId.New(), "F7 hide removes the testimonial from the landing page", Now.AddMinutes(2));
        await caregiversDb.SaveChangesAsync();

        Guid dangling = Guid.NewGuid();
        string json = "{\"refs\":[" +
            "{\"kind\":\"caregiver\",\"id\":\"" + visible.Id + "\"}," +
            "{\"kind\":\"caregiver\",\"id\":\"" + hidden.Id + "\"}," +
            "{\"kind\":\"caregiver\",\"id\":\"" + dangling + "\"}]}";

        LandingSection section = LandingSection.Create(
            LandingSectionType.Testimonials, "آراء", "Testimonials", contentJson: json);
        section.Publish();
        cmsDb.LandingSections.Add(section);
        await cmsDb.SaveChangesAsync();

        var published = await new GetPublishedLandingSectionsQueryHandler(cmsDb, familiesDb, caregiversDb)
            .Handle(new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(published.IsSuccess);
        LandingSectionPublicItem item = Assert.Single(published.Value);
        TestimonialItem testimonial = Assert.Single(item.Testimonials ?? []);
        Assert.Equal(visible.Id, testimonial.RefId);
        Assert.Equal(5, testimonial.Stars);
        Assert.Equal("Retained public testimonial", testimonial.Text);

        // Hiding removes the public testimonial without breaking the section:
        // only the genuinely absent ref may be reported as dangling.
        var admin = await new GetLandingAdminListQueryHandler(cmsDb, familiesDb, caregiversDb)
            .Handle(new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(admin.IsSuccess);
        Assert.Equal(1, admin.Value.Items.Single().DanglingRefCount);

        hidden.Restore(UserId.New(), "F7 restore brings the testimonial back", Now.AddMinutes(3));
        await caregiversDb.SaveChangesAsync();

        var restored = await new GetPublishedLandingSectionsQueryHandler(cmsDb, familiesDb, caregiversDb)
            .Handle(new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(restored.IsSuccess);
        Assert.Equal(2, Assert.Single(restored.Value).Testimonials?.Count);
    }

    private static readonly string[] ForbiddenReviewerTokens =
        ["reviewer", "familyname", "email", "phone", "avatar", "username", "displayname"];

    private static async Task<IActionResult> InvokeHideAsync(Guid ratingId, Result<bool> response) =>
        await CreateController(new StubSender { Response = response }, Guid.CreateVersion7())
            .Hide(ratingId, new ModerateCaregiverRatingRequest("Reason"), default);

    private static async Task<IActionResult> InvokeRestoreAsync(Guid ratingId, Result<bool> response) =>
        await CreateController(new StubSender { Response = response }, Guid.CreateVersion7())
            .Restore(ratingId, new ModerateCaregiverRatingRequest("Reason"), default);

    private static Task<Result<bool>> HideAsync(
        ICaregiversDbContext db, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        new HideAdminCaregiverRatingCommandHandler(db)
            .Handle(new HideAdminCaregiverRatingCommand(ratingId, actor, reason, utcNow), default);

    private static Task<Result<bool>> RestoreAsync(
        ICaregiversDbContext db, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        new RestoreAdminCaregiverRatingCommandHandler(db)
            .Handle(new RestoreAdminCaregiverRatingCommand(ratingId, actor, reason, utcNow), default);

    private static Task<Result<CaregiverRatingResponse>> RateAsync(
        ICaregiversDbContext db, Caregiver caregiver, UserId family, Guid bookingId,
        int stars, string? reviewText, DateTime utcNow) =>
        new RateCompletedCaregiverBookingCommandHandler(
                db, new StubEligibility(new AuthorizedCompletedCaregiverBooking(family.Value, caregiver.Id)))
            .Handle(new RateCompletedCaregiverBookingCommand(
                bookingId, UserId.New(), stars, reviewText, utcNow), default);

    private static async Task<Guid> RatingIdAsync(ICaregiversDbContext db, Guid bookingId) =>
        (await db.CaregiverRatings.SingleAsync(rating => rating.BookingId == bookingId)).Id;

    private static async Task<Caregiver> AddCaregiverAsync(CaregiversDbContext db)
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        db.Caregivers.Add(caregiver);
        await db.SaveChangesAsync();
        return caregiver;
    }

    private static async Task<CaregiverRating> AddRatingAsync(CaregiversDbContext db, int stars)
    {
        Caregiver caregiver = await AddCaregiverAsync(db);
        CaregiverRating rating = AddRating(db, caregiver, stars, null, Now);
        await db.SaveChangesAsync();
        return rating;
    }

    private static CaregiverRating AddRating(
        CaregiversDbContext db, Caregiver caregiver, int stars, string? reviewText, DateTime createdOnUtc)
    {
        CaregiverRating rating = CaregiverRating.Create(
            caregiver.Id, Guid.NewGuid(), FamilyId.New(), UserId.New(), stars, reviewText, createdOnUtc);
        db.CaregiverRatings.Add(rating);
        return rating;
    }

    private static CaregiversDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CmsDbContext CreateCmsDb()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static int[] ProducedStatusCodes(string action) =>
        [.. typeof(AdminCaregiverRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)];

    private static int? MapperStatus(Error error) =>
        ResultProblemDetailsMapper.Create(error, new DefaultHttpContext()).Status;

    private static AdminCaregiverRatingsController CreateController(StubSender sender, Guid subjectUserId) =>
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

    private static int? ProblemStatus(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(objectResult.StatusCode, problem.Status);
        return problem.Status;
    }

    private sealed class StubEligibility(AuthorizedCompletedCaregiverBooking booking)
        : ICaregiverBookingRatingEligibility
    {
        public Task<AuthorizedCompletedCaregiverBooking?> GetEligibleBookingAsync(
            Guid bookingId, UserId actorUserId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorizedCompletedCaregiverBooking?>(booking);
    }

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
