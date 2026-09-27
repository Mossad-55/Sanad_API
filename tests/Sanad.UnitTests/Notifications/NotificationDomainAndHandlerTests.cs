using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class NotificationDomainAndHandlerTests
{
    private static readonly Assembly ApiAssembly = typeof(NotificationsController).Assembly;
    private static readonly Type Notification = FindType("Sanad.Modules.Notifications.Domain.Notifications.Notification");
    private static readonly Type NotificationsDbContext = FindType("Sanad.Modules.Notifications.Infrastructure.Persistence.NotificationsDbContext");

    [Fact]
    public void DomainCreate_ShouldRejectMissingIdentifiersAndNonUtcTime()
    {
        Assert.Throws<TargetInvocationException>(() => Create(Guid.Empty, Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<TargetInvocationException>(() => Create(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow));
        Assert.Throws<TargetInvocationException>(() => Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.Now));
    }

    [Fact]
    public async Task List_ShouldDefaultToTwenty_IsolateRecipients_AndExcludeExpiredRows()
    {
        await using var db = CreateDb();
        var recipient = Guid.NewGuid();
        var time = DateTime.UtcNow.AddMinutes(-2);
        var rows = Enumerable.Range(0, 25).Select(i => Create(recipient, Guid.NewGuid(), time, type: $"type-{i}")).ToArray();
        foreach (var row in rows) Add(db, row);
        Add(db, Create(recipient, Guid.NewGuid(), DateTime.UtcNow.AddYears(-1).AddMinutes(-1), type: "expired"));
        Add(db, Create(Guid.NewGuid(), Guid.NewGuid(), time, type: "other"));
        await Save(db);

        var firstResult = await Handle("ListNotificationsQueryHandler", "ListNotificationsQuery", db, recipient, null, 20);
        var first = Property(firstResult, "Value");
        var items = ((System.Collections.IEnumerable)Property(first, "Items")!).Cast<object>().ToList();
        Assert.Equal(20, items.Count);
        Assert.NotNull(Property(first, "NextCursor"));
        Assert.Equal(25, Property(first, "UnreadCount"));
        Assert.DoesNotContain(items, item => (string)Property(item, "Type")! is "expired" or "other");
        Assert.Equal(items.OrderByDescending(item => (DateTime)Property(item, "CreatedOnUtc")!).ThenByDescending(item => (Guid)Property(item, "Id")!).Select(item => (Guid)Property(item, "Id")!), items.Select(item => (Guid)Property(item, "Id")!));

        var secondResult = await Handle("ListNotificationsQueryHandler", "ListNotificationsQuery", db, recipient, Property(first, "NextCursor"), 20);
        var second = Property(secondResult, "Value");
        var secondItems = ((System.Collections.IEnumerable)Property(second, "Items")!).Cast<object>().ToList();
        Assert.Equal(5, secondItems.Count);
        Assert.Empty(items.Select(item => Property(item, "Id")).Intersect(secondItems.Select(item => Property(item, "Id"))));
    }

    [Fact]
    public async Task ReadCommands_ShouldEnforceOwnership_AndRepeatSafely()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var notification = Create(owner, Guid.NewGuid(), DateTime.UtcNow);
        Add(db, notification); await Save(db);
        var unauthorized = await Handle("MarkNotificationReadCommandHandler", "MarkNotificationReadCommand", db, Guid.NewGuid(), Property(notification, "Id"));
        Assert.False((bool)Property(unauthorized, "IsSuccess")!);
        Assert.Equal("Notifications.NotFound", Property(Property(unauthorized, "Error"), "Code"));
        var first = await Handle("MarkNotificationReadCommandHandler", "MarkNotificationReadCommand", db, owner, Property(notification, "Id"));
        var readOn = Property(notification, "ReadOnUtc");
        var repeat = await Handle("MarkNotificationReadCommandHandler", "MarkNotificationReadCommand", db, owner, Property(notification, "Id"));
        Assert.True((bool)Property(first, "IsSuccess")!);
        Assert.True((bool)Property(repeat, "IsSuccess")!);
        Assert.Equal(readOn, Property(notification, "ReadOnUtc"));
    }

    [Fact]
    public async Task MarkAllRead_ShouldOnlyAffectOwnedCurrentUnreadRows_AndRepeatSafely()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var current = Create(owner, Guid.NewGuid(), DateTime.UtcNow);
        var expired = Create(owner, Guid.NewGuid(), DateTime.UtcNow.AddYears(-1).AddMinutes(-1));
        var other = Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        Add(db, current); Add(db, expired); Add(db, other); await Save(db);
        var first = await Handle("MarkAllNotificationsReadCommandHandler", "MarkAllNotificationsReadCommand", db, owner);
        var readOn = Property(current, "ReadOnUtc");
        var repeat = await Handle("MarkAllNotificationsReadCommandHandler", "MarkAllNotificationsReadCommand", db, owner);
        Assert.True((bool)Property(first, "IsSuccess")!); Assert.True((bool)Property(repeat, "IsSuccess")!);
        Assert.NotNull(readOn); Assert.Null(Property(expired, "ReadOnUtc")); Assert.Null(Property(other, "ReadOnUtc")); Assert.Equal(readOn, Property(current, "ReadOnUtc"));
    }

    private static object Create(Guid recipient, Guid destination, DateTime created, string category = "category", string type = "type")
        => Notification.GetMethod("Create")!.Invoke(null, [recipient, category, type, "title", "body", "Elderly", destination, created])!;

    private static async Task<object> Handle(string handlerName, string requestName, object db, params object?[] args)
    {
        var handlerType = FindType($"Sanad.Modules.Notifications.Application.Notifications.{handlerName}");
        var requestType = FindType($"Sanad.Modules.Notifications.Application.Notifications.{requestName}");
        var handler = Activator.CreateInstance(handlerType, db)!;
        var request = Activator.CreateInstance(requestType, args)!;
        var task = (Task)handlerType.GetMethod("Handle")!.Invoke(handler, [request, CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static object Property(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance)!;
    private static void Add(NotificationsDbContext db, object entity) => db.Notifications.Add((Notification)entity);
    private static Task Save(NotificationsDbContext db) => db.SaveChangesAsync(CancellationToken.None);
    private static NotificationsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotificationsDbContext(options);
    }
    private static Type FindType(string name)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type is not null);
        if (loaded is not null) return loaded;
        foreach (var assemblyName in new[] { "Sanad.Modules.Notifications.Domain", "Sanad.Modules.Notifications.Application", "Sanad.Modules.Notifications.Infrastructure" })
        {
            try
            {
                var type = Assembly.Load(assemblyName).GetType(name);
                if (type is not null) return type;
            }
            catch (FileNotFoundException) { }
        }
        throw new InvalidOperationException($"Could not load {name}.");
    }
}
