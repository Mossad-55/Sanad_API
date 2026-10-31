using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Feedback;
using Ahlena.Modules.Identity.Domain;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Portal second pass B3: the admin app-rating investigation read
/// (<c>GET /api/v1/admin/feedback/app-ratings</c>).
/// <para>
/// The controller contract is pinned by reflection (route, read-only
/// <c>OperationalReadAdmin</c> policy — SuperAdmin + SupportAdmin on a Normal
/// token; ContentAdmin, FinanceAdmin and app audiences stay denied through the
/// existing policy proven in <c>AdminOperationalReadAuthorizationTests</c>).
/// The handler is exercised against a disposable EF InMemory context for
/// empty/nonempty reads, newest-first ordering, silent paging clamps and the
/// stored-columns-only DTO boundary. No PostgreSQL is needed: the read is a
/// plain keyed list with a converted-column order, no value-typed predicates.
/// </para>
/// </summary>
public sealed class AdminFeedbackAppRatingsTests
{
    // ======================= controller contract pins ========================

    [Fact]
    public void Controller_IsPinnedToOperationalReadAdmin_AndIsReadOnly()
    {
        var controllerType = typeof(AdminFeedbackAppRatingsController);

        var route = Assert.Single(controllerType.GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/v1/admin/feedback/app-ratings", route.Template);

        var authorize = Assert.Single(
            controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(AuthorizationPolicies.OperationalReadAdmin, authorize.Policy);

        MethodInfo[] actions = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .ToArray();
        MethodInfo list = Assert.Single(actions);
        Assert.Equal(nameof(AdminFeedbackAppRatingsController.ListAppRatings), list.Name);
        Assert.Single(list.GetCustomAttributes<HttpMethodAttribute>(), a => a is HttpGetAttribute);
    }

    [Fact]
    public void ListAppRatings_BindsOnlyQueryPaging_AndPublishesOkWithDeniedStatuses()
    {
        MethodInfo list = typeof(AdminFeedbackAppRatingsController)
            .GetMethod(nameof(AdminFeedbackAppRatingsController.ListAppRatings))!;

        ParameterInfo[] parameters = list.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("page", parameters[0].Name);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(1, parameters[0].DefaultValue);
        Assert.True(parameters[0].GetCustomAttributes<FromQueryAttribute>().Any());
        Assert.Equal("pageSize", parameters[1].Name);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(20, parameters[1].DefaultValue);
        Assert.True(parameters[1].GetCustomAttributes<FromQueryAttribute>().Any());

        Assert.Equal<int>(
            [StatusCodes.Status200OK, StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden],
            list.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(a => a.StatusCode)
                .OrderBy(status => status)
                .ToArray());
        Assert.Contains(
            list.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            a => a.StatusCode == StatusCodes.Status200OK && a.Type == typeof(PagedAdminAppRatings));
    }

    // ============================ handler reads ==============================

    [Fact]
    public async Task Handler_ReturnsAnEmptyPage_WhenNoRatingsWereStoredYet()
    {
        await using IdentityTestDbContext db = CreateDb();

        Result<PagedAdminAppRatings> result =
            await new GetAdminAppRatingsQueryHandler(db).Handle(new(), default);

        PagedAdminAppRatings page = ValueOf(result);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task Handler_ReturnsStoredColumnsNewestFirst_WithNothingJoinedIn()
    {
        await using IdentityTestDbContext db = CreateDb();
        DateTime older = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        Feedback olderRating = NewRating(rating: 2, comment: "wants reminders", older);
        Feedback newerRating = NewRating(rating: 5, comment: null, newer);
        db.Feedbacks.AddRange(olderRating, newerRating);
        await db.SaveChangesAsync();

        PagedAdminAppRatings page = ValueOf(
            await new GetAdminAppRatingsQueryHandler(db).Handle(new(), default));

        Assert.Equal(2, page.TotalCount);

        // Newest first: the later submission is returned before the earlier one.
        AdminAppRatingResponse newest = page.Items[0];
        Assert.Equal(newerRating.Id, newest.Id);
        Assert.Equal(newerRating.UserId.Value, newest.UserId);
        Assert.Equal(newerRating.Rating, newest.Rating);
        Assert.Null(newest.Comment);
        Assert.Equal(newerRating.DeviceInfo, newest.DeviceInfo);
        Assert.Equal(newerRating.AppVersion, newest.AppVersion);
        Assert.Equal(newerRating.CreatedOnUtc, newest.CreatedOnUtc);

        AdminAppRatingResponse oldest = page.Items[1];
        Assert.Equal(olderRating.Id, oldest.Id);
        Assert.Equal(olderRating.UserId.Value, oldest.UserId);
        Assert.Equal("wants reminders", oldest.Comment);
        Assert.Equal(older, oldest.CreatedOnUtc);

        // Stored-columns-only boundary: the response payload carries no
        // identity/contact fields because the DTO project never joins any.
        string json = JsonSerializer.Serialize(page);
        Assert.False(
            json.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("nameArabic", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("nameEnglish", StringComparison.OrdinalIgnoreCase),
            $"The rating read leaked identity or contact fields: {json}");
    }

    [Fact]
    public async Task Handler_ClampsOutOfRangePagingSilently_AndSplitsPagesStably()
    {
        await using IdentityTestDbContext db = CreateDb();
        DateTime baseUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        List<Feedback> ratings = [];
        for (int i = 0; i < 3; i++)
        {
            ratings.Add(NewRating(rating: 3, comment: $"rating {i}", baseUtc.AddDays(i)));
        }

        db.Feedbacks.AddRange(ratings);
        await db.SaveChangesAsync();
        var handler = new GetAdminAppRatingsQueryHandler(db);

        // Below-range page and pageSize fall back to the defaults silently.
        PagedAdminAppRatings clamped = ValueOf(await handler.Handle(new(0, 0), default));
        Assert.Equal(1, clamped.Page);
        Assert.Equal(20, clamped.PageSize);
        Assert.Equal(3, clamped.TotalCount);
        Assert.Equal(3, clamped.Items.Count);

        // Above-range pageSize falls back to the default of 20 as well.
        clamped = ValueOf(await handler.Handle(new(1, 1000), default));
        Assert.Equal(20, clamped.PageSize);

        // The page maximum of 100 itself is accepted unchanged.
        clamped = ValueOf(await handler.Handle(new(1, 100), default));
        Assert.Equal(100, clamped.PageSize);

        // Split: page 1 holds the two newest ratings, page 2 the oldest one.
        PagedAdminAppRatings firstPage = ValueOf(await handler.Handle(new(1, 2), default));
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(ratings[2].Id, firstPage.Items[0].Id);
        Assert.Equal(ratings[1].Id, firstPage.Items[1].Id);

        PagedAdminAppRatings secondPage = ValueOf(await handler.Handle(new(2, 2), default));
        AdminAppRatingResponse only = Assert.Single(secondPage.Items);
        Assert.Equal(ratings[0].Id, only.Id);

        // A page beyond the data is empty, with the full total preserved.
        PagedAdminAppRatings beyond = ValueOf(await handler.Handle(new(9, 2), default));
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
    }

    // ================================ helpers ================================

    private static IdentityTestDbContext CreateDb() => new(
        new DbContextOptionsBuilder<IdentityTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Feedback NewRating(int rating, string? comment, DateTime createdOnUtc) => new(
        Guid.NewGuid(),
        UserId.New(),
        rating,
        comment,
        "Android 14",
        "1.4.0",
        createdOnUtc);

    private static PagedAdminAppRatings ValueOf(Result<PagedAdminAppRatings> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
