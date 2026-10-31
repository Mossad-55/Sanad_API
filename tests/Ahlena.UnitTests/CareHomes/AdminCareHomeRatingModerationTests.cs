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
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Discovery;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// F7 Care Home-rating visibility and moderation: the sibling of the caregiver
/// target, plus <c>keep</c> and the owner flag. Verifies the reused Care Homes
/// operational policy and pinned route contract, the pinned 404/400/409
/// precedence, that hiding/keeping resolves the rating's unresolved owner flag
/// with attribution in the same transaction, that a hidden rating leaves the
/// SQL-computed top-rated aggregate and returns on restore, that moderation
/// never mutates the retained rating content, that a family re-rating cannot
/// unhide, and that admin projections expose no reviewer identity.
/// </summary>
public sealed class AdminCareHomeRatingModerationTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Controller_RequiresCareHomesOperationalAdminPolicy_OnThePinnedRoute()
    {
        var authorize = typeof(AdminCareHomeRatingsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, authorize!.Policy);
        Assert.Equal(
            "api/v1/admin/care-homes/ratings",
            typeof(AdminCareHomeRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Empty(typeof(AdminCareHomeRatingsController)
            .GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(AdminCareHomeRatingsController.List), null, "GET")]
    [InlineData(nameof(AdminCareHomeRatingsController.Hide), "{ratingId:guid}/hide", "POST")]
    [InlineData(nameof(AdminCareHomeRatingsController.Keep), "{ratingId:guid}/keep", "POST")]
    [InlineData(nameof(AdminCareHomeRatingsController.Restore), "{ratingId:guid}/restore", "POST")]
    public void Controller_ActionTemplatesAndVerbsMatchThePinnedContract(
        string action, string? template, string verb)
    {
        HttpMethodAttribute attribute = typeof(AdminCareHomeRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(template, attribute.Template);
        Assert.Equal(verb, Assert.Single(attribute.HttpMethods));
    }

    [Fact]
    public void Controller_PublishesOnlyThePinnedStatusCodes()
    {
        Assert.Equal<int>([200, 400], ProducedStatusCodes(nameof(AdminCareHomeRatingsController.List)));
        Assert.Equal<int>([204, 400, 404, 409], ProducedStatusCodes(nameof(AdminCareHomeRatingsController.Hide)));
        Assert.Equal<int>([204, 400, 404, 409], ProducedStatusCodes(nameof(AdminCareHomeRatingsController.Keep)));
        Assert.Equal<int>([204, 400, 404, 409], ProducedStatusCodes(nameof(AdminCareHomeRatingsController.Restore)));
    }

    [Fact]
    public async Task Hide_SetsTheVisibilityFactsAndResolvesTheOpenFlagAsHidden()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);
        UserId owner = UserId.New();
        CareHomeRatingFlag flag = AddFlag(db, rating, owner, "Unfair review", Now.AddMinutes(-30));
        await db.SaveChangesAsync();
        UserId admin = UserId.New();

        var result = await HideAsync(db, rating.Id, admin, "  Untrue claims  ", Now.AddHours(1));

        Assert.True(result.IsSuccess);

        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(CareHomeRatingModerationState.Hidden, stored.ModerationState);
        Assert.Equal("Untrue claims", stored.HideReason);
        Assert.Equal(admin, stored.HiddenByUserId);
        Assert.Equal(Now.AddHours(1), stored.HiddenOnUtc);
        Assert.Equal(4, stored.Stars);
        Assert.Equal("Review", stored.ReviewText);

        CareHomeRatingModerationAction action = Assert.Single(
            await db.RatingModerationActions.ToListAsync());
        Assert.Equal(CareHomeRatingModerationActionType.Hidden, action.Action);
        Assert.Equal(admin, action.ActorUserId);
        Assert.Equal(Now.AddHours(1), action.OccurredOnUtc);
        Assert.Equal("Untrue claims", action.Reason);

        CareHomeRatingFlag resolved = await db.RatingFlags.SingleAsync(item => item.Id == flag.Id);
        Assert.Equal(CareHomeRatingFlagStatus.Resolved, resolved.Status);
        Assert.Equal(CareHomeRatingFlagResolution.Hidden, resolved.Resolution);
        Assert.Equal(admin, resolved.ResolvedByUserId);
        Assert.Equal(Now.AddHours(1), resolved.ResolvedOnUtc);
        Assert.Equal("Unfair review", resolved.Reason);
    }

    [Fact]
    public async Task Hide_WhenAlreadyHidden_IsAConflictAndWritesNothing()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();
        Assert.True((await HideAsync(db, rating.Id, admin, "First", Now.AddHours(1))).IsSuccess);

        var second = await HideAsync(db, rating.Id, admin, "Second", Now.AddHours(2));

        Assert.False(second.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.AlreadyHidden, second.Error);
        Assert.Equal(1, await db.RatingModerationActions.CountAsync());
        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal("First", stored.HideReason);
        Assert.Equal(Now.AddHours(1), stored.HiddenOnUtc);
    }

    [Fact]
    public async Task Restore_WhenVisible_IsAConflictAndWritesNothing()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);

        var result = await RestoreAsync(db, rating.Id, UserId.New(), "Nothing to restore", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.AlreadyVisible, result.Error);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
        Assert.Equal(
            CareHomeRatingModerationState.Visible,
            (await db.Ratings.SingleAsync(item => item.Id == rating.Id)).ModerationState);
    }

    [Fact]
    public async Task Keep_WithNoOpenFlag_IsAConflictAndChangesNothing()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);

        var result = await KeepAsync(db, rating.Id, UserId.New(), "Looks fine", Now.AddHours(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.NoOpenFlag, result.Error);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
        Assert.Equal(
            CareHomeRatingModerationState.Visible,
            (await db.Ratings.SingleAsync(item => item.Id == rating.Id)).ModerationState);
    }

    [Fact]
    public async Task Keep_ResolvesTheOpenFlagAsKeptAndLeavesTheRatingVisible()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);
        CareHomeRatingFlag flag = AddFlag(db, rating, UserId.New(), "Unfair review", Now);
        await db.SaveChangesAsync();
        UserId admin = UserId.New();

        var result = await KeepAsync(db, rating.Id, admin, "Review is accurate", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(CareHomeRatingModerationState.Visible, stored.ModerationState);
        Assert.Null(stored.HideReason);
        Assert.Null(stored.HiddenByUserId);
        Assert.Null(stored.HiddenOnUtc);

        // Keep changes no visibility, so it appends no moderation action row.
        Assert.Empty(await db.RatingModerationActions.ToListAsync());

        CareHomeRatingFlag resolved = await db.RatingFlags.SingleAsync(item => item.Id == flag.Id);
        Assert.Equal(CareHomeRatingFlagStatus.Resolved, resolved.Status);
        Assert.Equal(CareHomeRatingFlagResolution.Kept, resolved.Resolution);
        Assert.Equal(admin, resolved.ResolvedByUserId);
        Assert.Equal(Now.AddHours(1), resolved.ResolvedOnUtc);
    }

    [Fact]
    public async Task Hide_WithoutAFlag_StillHides()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 2);

        var result = await HideAsync(db, rating.Id, UserId.New(), "Policy breach", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.True((await db.Ratings.SingleAsync(item => item.Id == rating.Id)).IsHidden);
        Assert.Empty(await db.RatingFlags.ToListAsync());
    }

    [Fact]
    public async Task HideThenRestore_AppendsOneActionPerTransitionAndRetainsTheContent()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 3);
        UserId admin = UserId.New();

        Assert.True((await HideAsync(db, rating.Id, admin, "Hidden", Now.AddHours(1))).IsSuccess);
        Assert.True((await RestoreAsync(db, rating.Id, admin, "Appeal upheld", Now.AddHours(2))).IsSuccess);

        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(CareHomeRatingModerationState.Visible, stored.ModerationState);
        Assert.Null(stored.HiddenByUserId);
        Assert.Null(stored.HiddenOnUtc);
        Assert.Null(stored.HideReason);
        Assert.Equal(3, stored.Stars);
        Assert.Equal("Review", stored.ReviewText);

        List<CareHomeRatingModerationAction> actions = await db.RatingModerationActions
            .OrderBy(action => action.OccurredOnUtc)
            .ToListAsync();
        Assert.Equal(
            [CareHomeRatingModerationActionType.Hidden, CareHomeRatingModerationActionType.Restored],
            actions.Select(action => action.Action));
        Assert.All(actions, action => Assert.Equal(admin, action.ActorUserId));
        Assert.Equal("Hidden", actions[0].Reason);
        Assert.Equal("Appeal upheld", actions[1].Reason);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("keep")]
    [InlineData("restore")]
    public async Task Moderation_UnknownOrEmptyRating_ReturnsNotFound(string action)
    {
        await using CareHomesDbContext db = CreateDb();

        var missing = await ModerateAsync(db, action, Guid.NewGuid(), UserId.New(), "Reason", Now);
        var empty = await ModerateAsync(db, action, Guid.Empty, UserId.New(), "Reason", Now);

        Assert.Equal(CareHomeRatingModerationErrors.NotFound, missing.Error);
        Assert.Equal(CareHomeRatingModerationErrors.NotFound, empty.Error);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
        Assert.Empty(await db.RatingFlags.ToListAsync());
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("keep")]
    [InlineData("restore")]
    public async Task Moderation_InvalidReason_IsRejectedBeforeTheStateAndChangesNothing(string action)
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);

        foreach (string? reason in new string?[]
                 {
                     null, string.Empty, "   ", new string('x', CareHomeRating.MaximumModerationReasonLength + 1)
                 })
        {
            var result = await ModerateAsync(db, action, rating.Id, UserId.New(), reason, Now.AddHours(1));
            Assert.False(result.IsSuccess);
            Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, result.Error);
        }

        Assert.Equal(
            CareHomeRatingModerationState.Visible,
            (await db.Ratings.SingleAsync(item => item.Id == rating.Id)).ModerationState);
        Assert.Empty(await db.RatingModerationActions.ToListAsync());
    }

    [Fact]
    public async Task Moderation_BlankReason_PrefersTheReasonCodeOverTheStateCode()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating hiddenRating = await AddRatingAsync(db, stars: 4);
        CareHomeRating visibleRating = await AddRatingAsync(db, stars: 4);
        UserId admin = UserId.New();
        Assert.True((await HideAsync(db, hiddenRating.Id, admin, "First", Now.AddHours(1))).IsSuccess);

        // Every illegal-or-legal state still answers the reason code first: a
        // blank reason on an already-hidden rating is not AlreadyHidden, and a
        // blank reason on a visible rating is not AlreadyVisible/NoOpenFlag.
        var againHidden = await HideAsync(db, hiddenRating.Id, admin, "  ", Now.AddHours(2));
        var restoreVisible = await RestoreAsync(db, visibleRating.Id, admin, "  ", Now.AddHours(2));
        var keepWithoutFlag = await KeepAsync(db, visibleRating.Id, admin, "  ", Now.AddHours(2));

        Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, againHidden.Error);
        Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, restoreVisible.Error);
        Assert.Equal(CareHomeRatingModerationErrors.InvalidReason, keepWithoutFlag.Error);
    }

    [Fact]
    public async Task Moderation_ReasonOfExactlyTheBound_IsAccepted()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 4);
        string reason = new('r', CareHomeRating.MaximumModerationReasonLength);

        var result = await HideAsync(db, rating.Id, UserId.New(), reason, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == rating.Id);
        Assert.Equal(reason, stored.HideReason);
    }

    [Fact]
    public async Task FamilyEdit_CannotUnhideARatingAndKeepsTheRetainedContentEditable()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, "Rated home");
        FamilyId family = FamilyId.New();
        UserId actor = UserId.New();
        CareHomeBooking booking = AddBooking(db, facility.Id, family, actor, confirmed: true);
        await db.SaveChangesAsync();
        var rate = new RateCareHomeBookingCommandHandler(db);
        var created = await rate.Handle(new(booking.Id, actor, family, 3, "Good", Now), default);
        Guid ratingId = created.Value.RatingId;
        Assert.True((await HideAsync(db, ratingId, UserId.New(), "Hidden", Now.AddHours(1))).IsSuccess);

        var edit = await rate.Handle(
            new(booking.Id, actor, family, 1, "  Changed my mind  ", Now.AddHours(2)), default);

        Assert.True(edit.IsSuccess);
        CareHomeRating stored = await db.Ratings.SingleAsync(item => item.Id == ratingId);
        Assert.True(stored.IsHidden);
        Assert.Equal(1, stored.Stars);
        Assert.Equal("Changed my mind", stored.ReviewText);
        Assert.Single(await db.RatingModerationActions.ToListAsync());
    }

    [Fact]
    public async Task TopRatedAggregate_ExcludesHiddenRatingsAndAgreesAfterRestore()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, "Rated home");
        AddRating(db, facility, 5, "Top", Now.AddDays(-2));
        AddRating(db, facility, 5, "Top too", Now.AddDays(-1));
        CareHomeRating low = AddRating(db, facility, 1, "Poor", Now);
        await db.SaveChangesAsync();
        var top = new GetTopRatedCareHomesQueryHandler(db, new FixedClock(Now));

        var before = await top.Handle(new(), default);
        var hidden = await HideAsync(db, low.Id, UserId.New(), "Hidden", Now.AddHours(1));
        var afterHide = await top.Handle(new(), default);
        var restored = await RestoreAsync(db, low.Id, UserId.New(), "Restored", Now.AddHours(2));
        var afterRestore = await top.Handle(new(), default);

        TopRatedCareHomeCard initial = Assert.Single(before.Value);
        Assert.Equal(facility.Id.Value, initial.CareHomeId);
        Assert.Equal(3, initial.ReviewsCount);
        Assert.Equal(3.67m, initial.AverageRating);

        // Hidden ratings leave the SQL-computed aggregate ...
        Assert.True(hidden.IsSuccess);
        TopRatedCareHomeCard hiddenCard = Assert.Single(afterHide.Value);
        Assert.Equal(2, hiddenCard.ReviewsCount);
        Assert.Equal(5m, hiddenCard.AverageRating);

        // ... and return to it on restore.
        Assert.True(restored.IsSuccess);
        TopRatedCareHomeCard restoredCard = Assert.Single(afterRestore.Value);
        Assert.Equal(3, restoredCard.ReviewsCount);
        Assert.Equal(3.67m, restoredCard.AverageRating);
    }

    [Fact]
    public async Task AdminList_FiltersOrdersAndPagesUnresolvedFlaggedFirst()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility first = await AddApproved(db, "First");
        CareHomeFacility second = await AddApproved(db, "Second");
        CareHomeRating oldest = AddRating(db, first, 5, "Oldest", Now.AddDays(-3));
        CareHomeRating middle = AddRating(db, first, 4, "Middle", Now.AddDays(-2));
        CareHomeRating newest = AddRating(db, first, 4, "Newest", Now.AddDays(-1));
        CareHomeRating other = AddRating(db, second, 2, "Other facility", Now);
        AddFlag(db, middle, UserId.New(), "Unfair", Now.AddMinutes(-10));
        await db.SaveChangesAsync();
        Assert.True((await HideAsync(db, other.Id, UserId.New(), "Hidden", Now)).IsSuccess);
        var handler = new GetAdminCareHomeRatingsQueryHandler(db);

        var all = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, null, null, 1, 25), default);
        var hidden = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, "hidden", null, 1, 25), default);
        var visible = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, "visible", null, 1, 25), default);
        var flagged = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, null, true, 1, 25), default);
        var unflagged = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, null, false, 1, 25), default);
        var byStars = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, 4, "all", null, 1, 25), default);
        var byFacility = await handler.Handle(new GetAdminCareHomeRatingsQuery(second.Id.Value, null, null, null, 1, 25), default);
        var firstPage = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, "all", null, 1, 2), default);
        var secondPage = await handler.Handle(new GetAdminCareHomeRatingsQuery(null, null, "all", null, 2, 2), default);

        Assert.Equal(4, all.Value.TotalCount);
        Assert.Equal(25, all.Value.PageSize);
        Assert.Equal(
            [middle.Id, other.Id, newest.Id, oldest.Id],
            all.Value.Items.Select(item => item.RatingId));

        AdminCareHomeRatingListItem hiddenItem = Assert.Single(hidden.Value.Items);
        Assert.Equal(other.Id, hiddenItem.RatingId);
        Assert.Equal("hidden", hiddenItem.ModerationState);
        Assert.Equal(1, hidden.Value.TotalCount);
        Assert.Equal(3, visible.Value.TotalCount);
        Assert.Equal(middle.Id, Assert.Single(flagged.Value.Items).RatingId);
        Assert.Equal([other.Id, newest.Id, oldest.Id], unflagged.Value.Items.Select(item => item.RatingId));
        Assert.Equal([middle.Id, newest.Id], byStars.Value.Items.Select(item => item.RatingId));
        Assert.Equal(other.Id, Assert.Single(byFacility.Value.Items).RatingId);
        Assert.Equal([middle.Id, other.Id], firstPage.Value.Items.Select(item => item.RatingId));
        Assert.Equal([newest.Id, oldest.Id], secondPage.Value.Items.Select(item => item.RatingId));
        Assert.Equal(4, firstPage.Value.TotalCount);
        Assert.Equal(4, secondPage.Value.TotalCount);
        Assert.True(firstPage.Value.Items.Select(item => item.RatingId)
            .Intersect(secondPage.Value.Items.Select(item => item.RatingId)).Count() == 0);
    }

    [Fact]
    public async Task AdminList_ItemCarriesTheAttributionAndFlagAndNoReviewerIdentity()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 3);
        CareHomeRating flagged = await AddRatingAsync(db, stars: 2);
        UserId admin = UserId.New();
        CareHomeRatingFlag flag = CareHomeRatingFlag.Open(
            flagged.Id, flagged.CareHomeId, UserId.New(), "Owner complaint", Now.AddMinutes(-20));
        db.RatingFlags.Add(flag);
        await db.SaveChangesAsync();
        Assert.True((await HideAsync(db, rating.Id, admin, "Abusive wording", Now.AddHours(1))).IsSuccess);

        var result = await new GetAdminCareHomeRatingsQueryHandler(db).Handle(
            new GetAdminCareHomeRatingsQuery(null, null, "all", null, 1, 25), default);

        Assert.Equal(2, result.Value.TotalCount);
        AdminCareHomeRatingListItem item = result.Value.Items.Single(candidate => candidate.RatingId == rating.Id);
        Assert.Equal(rating.CareHomeId.Value, item.FacilityId);
        Assert.Equal(rating.BookingId, item.BookingId);
        Assert.Equal(rating.FamilyId.Value, item.FamilyId);
        Assert.Equal(3, item.Stars);
        Assert.Equal("hidden", item.ModerationState);
        Assert.Equal<Guid?>(admin.Value, item.HiddenByUserId);
        Assert.Equal(Now.AddHours(1), item.HiddenOnUtc);
        Assert.Equal("Abusive wording", item.HideReason);
        Assert.Equal(rating.CreatedOnUtc, item.CreatedOnUtc);
        Assert.Equal(rating.UpdatedOnUtc, item.UpdatedOnUtc);

        // Hiding a rating that carries an open owner flag resolves that flag, so
        // the admin projection no longer reports it as unresolved.
        Assert.Null(item.UnresolvedFlagId);
        Assert.Null(item.UnresolvedFlagReason);
        Assert.Null(item.UnresolvedFlaggedOnUtc);

        AdminCareHomeRatingListItem unresolved = result.Value.Items
            .Single(candidate => candidate.RatingId == flagged.Id);
        Assert.Equal("visible", unresolved.ModerationState);
        Assert.Equal<Guid?>(flag.Id, unresolved.UnresolvedFlagId);
        Assert.Equal("Owner complaint", unresolved.UnresolvedFlagReason);
        Assert.Equal(Now.AddMinutes(-20), unresolved.UnresolvedFlaggedOnUtc);

        string[] published = typeof(AdminCareHomeRatingListItem).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            [
                "RatingId", "FacilityId", "BookingId", "FamilyId", "Stars", "ReviewText",
                "CreatedOnUtc", "UpdatedOnUtc", "ModerationState", "HiddenByUserId", "HiddenOnUtc",
                "HideReason", "UnresolvedFlagId", "UnresolvedFlagReason", "UnresolvedFlaggedOnUtc"
            ],
            published);
        Assert.DoesNotContain(published, name => ForbiddenReviewerTokens.Any(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AdminList_OpenFlagIsProjectedWhileUnresolvedAndClearedAfterKeep()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeRating rating = await AddRatingAsync(db, stars: 3);
        CareHomeRatingFlag flag = AddFlag(db, rating, UserId.New(), "Owner complaint", Now.AddMinutes(-20));
        await db.SaveChangesAsync();
        var handler = new GetAdminCareHomeRatingsQueryHandler(db);

        AdminCareHomeRatingListItem open = Assert.Single((await handler.Handle(
            new GetAdminCareHomeRatingsQuery(null, null, null, null, 1, 25), default)).Value.Items);
        Assert.Equal(flag.Id, open.UnresolvedFlagId);
        Assert.Equal("Owner complaint", open.UnresolvedFlagReason);
        Assert.Equal(Now.AddMinutes(-20), open.UnresolvedFlaggedOnUtc);

        Assert.True((await KeepAsync(db, rating.Id, UserId.New(), "Kept", Now)).IsSuccess);

        AdminCareHomeRatingListItem kept = Assert.Single((await handler.Handle(
            new GetAdminCareHomeRatingsQuery(null, null, null, null, 1, 25), default)).Value.Items);
        Assert.Null(kept.UnresolvedFlagId);
        Assert.Null(kept.UnresolvedFlagReason);
        Assert.Null(kept.UnresolvedFlaggedOnUtc);
        Assert.Equal("visible", kept.ModerationState);
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
        await using CareHomesDbContext db = CreateDb();

        var result = await new GetAdminCareHomeRatingsQueryHandler(db).Handle(
            new GetAdminCareHomeRatingsQuery(null, stars, moderation, null, page, pageSize), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CareHomeRatingModerationErrors.InvalidQuery, result.Error);
    }

    [Fact]
    public async Task Controller_Hide_ForwardsTheCommandAndReturnsNoContent()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        Guid ratingId = Guid.CreateVersion7();
        Guid actorUserId = Guid.CreateVersion7();

        IActionResult result = await CreateController(sender, actorUserId)
            .Hide(ratingId, new ModerateCareHomeRatingRequest("Reason"), default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<HideAdminCareHomeRatingCommand>(sender.LastRequest);
        Assert.Equal(ratingId, command.RatingId);
        Assert.Equal(new UserId(actorUserId), command.ActorUserId);
        Assert.Equal("Reason", command.Reason);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("keep")]
    [InlineData("restore")]
    public async Task Controller_FailureStatusesMatchThePinnedMapping(string action)
    {
        Guid ratingId = Guid.CreateVersion7();

        Assert.Equal(
            StatusCodes.Status404NotFound,
            ProblemStatus(await InvokeAsync(
                action, ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.NotFound))));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            ProblemStatus(await InvokeAsync(
                action, ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeAsync(
                action, ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.NoOpenFlag))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeAsync(
                action, ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyHidden))));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ProblemStatus(await InvokeAsync(
                action, ratingId, Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyVisible))));

        IActionResult success = await InvokeAsync(action, ratingId, Result<bool>.Success(true));
        Assert.IsType<NoContentResult>(success);
    }

    [Fact]
    public async Task Controller_ModerationWithoutTheSubjectClaim_ReturnsUnauthorized()
    {
        var sender = new StubSender { Response = Result<bool>.Success(true) };
        AdminCareHomeRatingsController controller = CreateController(sender, Guid.Empty);
        Guid ratingId = Guid.CreateVersion7();
        var request = new ModerateCareHomeRatingRequest("Reason");

        Assert.IsType<UnauthorizedResult>(await controller.Hide(ratingId, request, default));
        Assert.IsType<UnauthorizedResult>(await controller.Keep(ratingId, request, default));
        Assert.IsType<UnauthorizedResult>(await controller.Restore(ratingId, request, default));
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public void Mapper_PinsTheModerationStatusCodes()
    {
        Assert.Equal(404, MapperStatus(CareHomeRatingModerationErrors.NotFound));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidReason));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidQuery));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.AlreadyHidden));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.AlreadyVisible));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.NoOpenFlag));
    }

    private static readonly string[] ForbiddenReviewerTokens =
        ["reviewer", "familyname", "email", "phone", "avatar", "username", "displayname", "owneruserid"];

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

    private static Task<Result<bool>> ModerateAsync(
        ICareHomesDbContext db, string action, Guid ratingId, UserId actor, string? reason, DateTime utcNow) =>
        action switch
        {
            "hide" => HideAsync(db, ratingId, actor, reason, utcNow),
            "keep" => KeepAsync(db, ratingId, actor, reason, utcNow),
            _ => RestoreAsync(db, ratingId, actor, reason, utcNow)
        };

    private static Task<IActionResult> InvokeAsync(string action, Guid ratingId, Result<bool> response)
    {
        AdminCareHomeRatingsController controller =
            CreateController(new StubSender { Response = response }, Guid.CreateVersion7());
        var request = new ModerateCareHomeRatingRequest("Reason");

        return action switch
        {
            "hide" => controller.Hide(ratingId, request, default),
            "keep" => controller.Keep(ratingId, request, default),
            _ => controller.Restore(ratingId, request, default)
        };
    }

    private static async Task<CareHomeRating> AddRatingAsync(CareHomesDbContext db, int stars)
    {
        CareHomeFacility facility = await AddApproved(db, $"Home {Guid.NewGuid():N}");
        CareHomeRating rating = AddRating(db, facility, stars, "Review", Now);
        await db.SaveChangesAsync();
        return rating;
    }

    private static CareHomeRating AddRating(
        CareHomesDbContext db, CareHomeFacility facility, int stars, string? reviewText, DateTime createdOnUtc)
    {
        CareHomeRating rating = CareHomeRating.Create(
            Guid.NewGuid(), facility.Id, FamilyId.New(), UserId.New(), stars, reviewText, createdOnUtc);
        db.Ratings.Add(rating);
        return rating;
    }

    private static CareHomeRatingFlag AddFlag(
        CareHomesDbContext db, CareHomeRating rating, UserId owner, string reason, DateTime createdOnUtc)
    {
        CareHomeRatingFlag flag = CareHomeRatingFlag.Open(
            rating.Id, rating.CareHomeId, owner, reason, createdOnUtc);
        db.RatingFlags.Add(flag);
        return flag;
    }

    private static CareHomeBooking AddBooking(
        CareHomesDbContext db, CareHomeId facilityId, FamilyId familyId, UserId actor, bool confirmed)
    {
        var booking = CareHomeBooking.Create(facilityId, actor, familyId, ElderlyId.New(), Guid.NewGuid(),
            DateOnly.FromDateTime(Now.AddDays(3)), 1000, 0, 0, 1, "الاسم", "Name", 70, null,
            "Contact", null, null, null, Now);
        booking.MarkPaid(100, Now);
        booking.Accept(Now);
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now);
        booking.RecordCheckIn(actor, Now);
        if (confirmed) booking.ConfirmFamilyCheckIn(actor, Now);
        db.Bookings.Add(booking);
        return booking;
    }

    private static async Task<CareHomeFacility> AddApproved(CareHomesDbContext db, string name)
    {
        UserId owner = UserId.New();
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
        [.. typeof(AdminCareHomeRatingsController)
            .GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)];

    private static int? MapperStatus(Error error) =>
        ResultProblemDetailsMapper.Create(error, new DefaultHttpContext()).Status;

    private static AdminCareHomeRatingsController CreateController(StubSender sender, Guid subjectUserId) =>
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

    private static int StatusOf(IActionResult result) =>
        result switch
        {
            NoContentResult => StatusCodes.Status204NoContent,
            ObjectResult objectResult => objectResult.StatusCode ?? 0,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => -1
        };

    private static int ProblemStatus(IActionResult result) =>
        StatusOf(result);

    private sealed class FixedClock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = now;
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
