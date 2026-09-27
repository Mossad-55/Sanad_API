using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class AdminNotificationQueryTests
{
    [Fact]
    public async Task List_FiltersByCategoryTypeAndInclusiveUtcDatesAndPagesNewestFirst()
    {
        await using var db = CreateDb();
        var firstDay = new DateOnly(2026, 8, 3);
        var from = Utc(firstDay, new TimeOnly(0, 0));
        var through = Utc(firstDay.AddDays(1), new TimeOnly(23, 59, 59));
        var matchingOld = Create("Sos", "Created", from);
        var matchingNew = Create("Sos", "Created", through);
        var wrongCategory = Create("Help", "Created", through);
        var wrongType = Create("Sos", "Resolved", through);
        var before = Create("Sos", "Created", from.AddTicks(-1));
        var after = Create("Sos", "Created", Utc(firstDay.AddDays(2), TimeOnly.MinValue));
        db.Notifications.AddRange(matchingOld, matchingNew, wrongCategory, wrongType, before, after);
        await db.SaveChangesAsync();

        var handler = new ListAdminNotificationsQueryHandler(db);
        var page = (await handler.Handle(new ListAdminNotificationsQuery(1, 1, " Sos ", " Created ", firstDay, firstDay.AddDays(1)), CancellationToken.None)).Value;
        var nextPage = (await handler.Handle(new ListAdminNotificationsQuery(2, 1, "Sos", "Created", firstDay, firstDay.AddDays(1)), CancellationToken.None)).Value;

        Assert.Single(page.Items);
        Assert.Equal(matchingNew.Id, page.Items[0].Id);
        Assert.True(page.HasMore);
        Assert.Single(nextPage.Items);
        Assert.Equal(matchingOld.Id, nextPage.Items[0].Id);
        Assert.False(nextPage.HasMore);
    }

    [Fact]
    public async Task List_SecondPageDoesNotRepeatRows()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, 3)
            .Select(i => Create("Help", $"Type{i}", now.AddMinutes(-i)))
            .ToArray();
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();
        var handler = new ListAdminNotificationsQueryHandler(db);

        var first = (await handler.Handle(new ListAdminNotificationsQuery(1, 2), CancellationToken.None)).Value;
        var second = (await handler.Handle(new ListAdminNotificationsQuery(2, 2), CancellationToken.None)).Value;

        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
    }

    [Fact]
    public async Task List_RejectsOutOfBoundsPagingAndInvalidDateRanges()
    {
        await using var db = CreateDb();
        var handler = new ListAdminNotificationsQueryHandler(db);
        foreach (var query in new[]
                 {
                     new ListAdminNotificationsQuery(0, 20),
                     new ListAdminNotificationsQuery(1, 0),
                     new ListAdminNotificationsQuery(101, 20),
                     new ListAdminNotificationsQuery(1, 101),
                     new ListAdminNotificationsQuery(1, 20, StartDate: new DateOnly(2026, 8, 1), EndDate: new DateOnly(2026, 9, 1))
                 })
        {
            var result = await handler.Handle(query, CancellationToken.None);
            Assert.False(result.IsSuccess);
        }
    }

    [Fact]
    public void ListValidator_EnforcesPageBoundsAndInclusive31DayDateRange()
    {
        var validator = new ListAdminNotificationsQueryValidator();

        Assert.True(validator.TestValidate(new ListAdminNotificationsQuery(1, 1)).IsValid);
        Assert.True(validator.TestValidate(new ListAdminNotificationsQuery(100, 100,
            StartDate: new DateOnly(2026, 8, 1), EndDate: new DateOnly(2026, 8, 31))).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(0, 20)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(101, 20)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(1, 0)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(1, 101)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(1, 20,
            StartDate: new DateOnly(2026, 8, 1), EndDate: new DateOnly(2026, 9, 1))).IsValid);
    }

    [Fact]
    public async Task Detail_ReturnsMetadataAndHidesRowsOlderThanOneYear()
    {
        await using var db = CreateDb();
        var current = Create("Sos", "Created", DateTime.UtcNow.AddDays(-2));
        var expired = Create("Old", "Expired", DateTime.UtcNow.AddYears(-1).AddMinutes(-2));
        db.Notifications.AddRange(current, expired);
        await db.SaveChangesAsync();
        var handler = new GetAdminNotificationQueryHandler(db);

        var found = await handler.Handle(new GetAdminNotificationQuery(current.Id), CancellationToken.None);
        var missing = await handler.Handle(new GetAdminNotificationQuery(Guid.NewGuid()), CancellationToken.None);
        var tooOld = await handler.Handle(new GetAdminNotificationQuery(expired.Id), CancellationToken.None);

        Assert.True(found.IsSuccess);
        Assert.Equal(current.Id, found.Value.Id);
        Assert.False(missing.IsSuccess);
        Assert.False(tooOld.IsSuccess);
    }

    [Fact]
    public async Task Timeline_UsesInclusiveUtcDatesAndAscendingStableOrderWithinRetention()
    {
        await using var db = CreateDb();
        var start = new DateOnly(2026, 8, 3);
        var end = start.AddDays(1);
        var first = Create("A", "AtStart", Utc(start, TimeOnly.MinValue));
        var middle = Create("B", "Middle", Utc(start, new TimeOnly(12, 30)));
        var last = Create("C", "AtEnd", Utc(end, new TimeOnly(23, 59, 59)));
        var before = Create("A", "Before", Utc(start, TimeOnly.MinValue).AddTicks(-1));
        var after = Create("A", "After", Utc(end.AddDays(1), TimeOnly.MinValue));
        var expired = Create("A", "Expired", DateTime.UtcNow.AddYears(-1).AddDays(-1));
        db.Notifications.AddRange(first, middle, last, before, after, expired);
        await db.SaveChangesAsync();

        var result = await new GetAdminNotificationTimelineQueryHandler(db).Handle(
            new GetAdminNotificationTimelineQuery(start, end), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { first.Id, middle.Id, last.Id }, result.Value.Items.Select(x => x.Id));
        Assert.Equal(new[] { Utc(start, TimeOnly.MinValue), Utc(start, new TimeOnly(12, 30)), Utc(end, new TimeOnly(23, 59, 59)) },
            result.Value.Items.Select(x => x.CreatedOnUtc));
        Assert.False(result.Value.HasMore);
        Assert.Equal(1, result.Value.Page);
    }

    [Fact]
    public async Task Timeline_IsPagedAndReportsMoreRows()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2026, 8, 3);
        var records = Enumerable.Range(0, 3).Select(i => Create("Sos", $"Type{i}", Utc(date, new TimeOnly(i, 0)))).ToArray();
        db.Notifications.AddRange(records);
        await db.SaveChangesAsync();

        var result = await new GetAdminNotificationTimelineQueryHandler(db).Handle(
            new GetAdminNotificationTimelineQuery(date, date, 1, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(records.Take(2).Select(x => x.Id), result.Value.Items.Select(x => x.Id));
        Assert.Equal(2, result.Value.Items.Count);
        Assert.True(result.Value.HasMore);
    }

    [Fact]
    public async Task Timeline_RejectsReversedAndLongerThan31InclusiveDays()
    {
        await using var db = CreateDb();
        var handler = new GetAdminNotificationTimelineQueryHandler(db);
        var start = new DateOnly(2026, 8, 1);

        Assert.True((await handler.Handle(new GetAdminNotificationTimelineQuery(start, start.AddDays(30)), CancellationToken.None)).IsSuccess);
        Assert.False((await handler.Handle(new GetAdminNotificationTimelineQuery(start, start.AddDays(31)), CancellationToken.None)).IsSuccess);
        Assert.False((await handler.Handle(new GetAdminNotificationTimelineQuery(start.AddDays(1), start), CancellationToken.None)).IsSuccess);
        Assert.False((await handler.Handle(new GetAdminNotificationTimelineQuery(start, start, 0, 10), CancellationToken.None)).IsSuccess);

        var validator = new GetAdminNotificationTimelineQueryValidator();
        Assert.True(validator.TestValidate(new GetAdminNotificationTimelineQuery(start, start.AddDays(30))).IsValid);
        Assert.False(validator.TestValidate(new GetAdminNotificationTimelineQuery(start, start.AddDays(31))).IsValid);
        Assert.False(validator.TestValidate(new GetAdminNotificationTimelineQuery(start.AddDays(1), start)).IsValid);
    }

    [Fact]
    public async Task Aggregate_ReturnsTotalReadUnreadAndCategoryCountsAfterDateFiltering()
    {
        await using var db = CreateDb();
        var start = new DateOnly(2026, 8, 10);
        var includedRead = Create("Sos", "Created", Utc(start, new TimeOnly(10, 0)));
        includedRead.MarkRead(Utc(start, new TimeOnly(10, 1)));
        var includedUnread = Create("Help", "Opened", Utc(start, new TimeOnly(11, 0)));
        var includedOther = Create("Sos", "Resolved", Utc(start.AddDays(1), new TimeOnly(9, 0)));
        var excludedBefore = Create("Outside", "Before", Utc(start.AddDays(-1), new TimeOnly(23, 59)));
        var excludedAfter = Create("Outside", "After", Utc(start.AddDays(2), TimeOnly.MinValue));
        var expired = Create("Outside", "Expired", DateTime.UtcNow.AddYears(-1).AddDays(-1));
        db.Notifications.AddRange(includedRead, includedUnread, includedOther, excludedBefore, excludedAfter, expired);
        await db.SaveChangesAsync();

        var result = await new GetAdminNotificationAggregateQueryHandler(db).Handle(
            new GetAdminNotificationAggregateQuery(start, start.AddDays(1)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(1, result.Value.ReadCount);
        Assert.Equal(2, result.Value.UnreadCount);
        Assert.Equal(new[] { new AdminNotificationCategoryCount("Help", 1), new AdminNotificationCategoryCount("Sos", 2) },
            result.Value.CategoryCounts);
    }

    [Fact]
    public async Task Aggregate_RejectsInvalidDateRange()
    {
        await using var db = CreateDb();
        var result = await new GetAdminNotificationAggregateQueryHandler(db).Handle(
            new GetAdminNotificationAggregateQuery(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(new GetAdminNotificationAggregateQueryValidator().TestValidate(
            new GetAdminNotificationAggregateQuery(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1))).IsValid);
    }

    [Fact]
    public void ResponseProjection_ExcludesRecipientPayloadAndDestinationFields()
    {
        var names = typeof(AdminNotificationRecord).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[] { "Id", "Category", "Type", "CreatedOnUtc", "ReadOnUtc" }.ToHashSet(StringComparer.Ordinal), names);
        Assert.DoesNotContain("RecipientUserId", names);
        Assert.DoesNotContain("Title", names);
        Assert.DoesNotContain("Body", names);
        Assert.DoesNotContain("DestinationEntityKind", names);
        Assert.DoesNotContain("DestinationEntityId", names);
    }

    private static Notification Create(string category, string type, DateTime createdOnUtc)
        => Notification.Create(Guid.NewGuid(), category, type, "private title", "private body", "PrivateDestination", Guid.NewGuid(), createdOnUtc);

    private static DateTime Utc(DateOnly date, TimeOnly time) => date.ToDateTime(time, DateTimeKind.Utc);

    private static NotificationsDbContext CreateDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
