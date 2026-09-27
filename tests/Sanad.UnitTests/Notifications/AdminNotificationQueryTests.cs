using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class AdminNotificationQueryTests
{
    [Fact]
    public async Task List_ReturnsNewestRowsWithinOneYearWithStablePaging()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var older = Notification.Create(Guid.NewGuid(), "CheckIn", "Negative", "private title", "private body", "Elderly", Guid.NewGuid(), now.AddDays(-5));
        var newer = Notification.Create(Guid.NewGuid(), "Sos", "Created", "private title", "private body", "ElderlySos", Guid.NewGuid(), now.AddDays(-1));
        var expired = Notification.Create(Guid.NewGuid(), "Old", "Expired", "private title", "private body", "Elderly", Guid.NewGuid(), now.AddYears(-1).AddMinutes(-1));
        db.Notifications.AddRange(older, newer, expired);
        await db.SaveChangesAsync();

        var handler = new ListAdminNotificationsQueryHandler(db);
        var page = (await handler.Handle(new ListAdminNotificationsQuery(1, 1), CancellationToken.None)).Value;

        Assert.Single(page.Items);
        Assert.Equal(newer.Id, page.Items[0].Id);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.True(page.HasMore);
        Assert.DoesNotContain(page.Items, item => item.Id == expired.Id);
    }

    [Fact]
    public async Task List_SecondPageDoesNotRepeatRows()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, 3)
            .Select(i => Notification.Create(Guid.NewGuid(), "Help", $"Type{i}", "title", "body", "HelpRequest", Guid.NewGuid(), now.AddMinutes(-i)))
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
    public void Validator_EnforcesPositivePageAndPageSizeAtMostOneHundred()
    {
        var validator = new ListAdminNotificationsQueryValidator();

        Assert.True(validator.TestValidate(new ListAdminNotificationsQuery(1, 1)).IsValid);
        Assert.True(validator.TestValidate(new ListAdminNotificationsQuery(100_000, 100)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(0, 20)).IsValid);
        Assert.False(validator.TestValidate(new ListAdminNotificationsQuery(1, 101)).IsValid);
    }

    private static NotificationsDbContext CreateDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
