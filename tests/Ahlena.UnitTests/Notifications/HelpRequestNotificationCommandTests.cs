using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Notifications;

public sealed class HelpRequestNotificationCommandTests
{
    [Fact]
    public async Task Handler_PersistsDurableNotificationsWithStableIdempotencyKeys()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var first = UserId.New(); var second = UserId.New(); var requestId = Guid.NewGuid();
        // F8: recipient copy comes from the batch language lookup, so one
        // recipient persists Arabic (the default) and one English.
        var languages = new FixedNotificationLanguageGateway(new Dictionary<Guid, UiLanguage>
        {
            [second.Value] = UiLanguage.English
        });
        var handler = new CreateHelpRequestAlertNotificationsCommandHandler(db, new Recipients(first, second), languages);
        var command = new CreateHelpRequestAlertNotificationsCommand(new ElderlyRecipient(UserId.New(), Guid.NewGuid()), requestId, DateTime.UtcNow);

        var firstResult = await handler.Handle(command, default);
        var retryResult = await handler.Handle(command, default);

        Assert.True(firstResult.IsSuccess, firstResult.IsFailure ? firstResult.Error.Code : null);
        Assert.True(retryResult.IsSuccess, retryResult.IsFailure ? retryResult.Error.Code : null);
        Assert.Equal(2, firstResult.Value);
        Assert.Equal(0, retryResult.Value);
        var rows = await db.Notifications.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(x => x.IdempotencyKey).Distinct().Count());

        // F8: one batch lookup answers both recipients, each row is bilingual
        // per its own recipient and still routes to the structured request id.
        // Exactly one batch per invocation: the first call and the idempotent
        // replay each resolve copy once, never once per recipient.
        Assert.Equal(2, languages.LookupCount);
        Assert.Equal(new[] { first.Value, second.Value }.OrderBy(x => x), languages.RequestedIds.OrderBy(x => x));
        var arabicRow = Assert.Single(rows, x => x.RecipientUserId == first.Value);
        var englishRow = Assert.Single(rows, x => x.RecipientUserId == second.Value);
        Assert.Equal(NotificationTextCatalog.HelpRequest(UiLanguage.Arabic).Title, arabicRow.Title);
        Assert.Equal(NotificationTextCatalog.HelpRequest(UiLanguage.Arabic).Body, arabicRow.Body);
        Assert.Equal(NotificationTextCatalog.HelpRequest(UiLanguage.English).Title, englishRow.Title);
        Assert.Equal(NotificationTextCatalog.HelpRequest(UiLanguage.English).Body, englishRow.Body);
        Assert.All(rows, row =>
        {
            Assert.Equal("ElderlyHelpRequest", row.Category);
            Assert.Equal("HelpRequestCreated", row.Type);
            Assert.Equal("HelpRequest", row.DestinationEntityKind);
            Assert.Equal(requestId, row.DestinationEntityId);
            Assert.DoesNotContain(requestId.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(requestId.ToString("N"), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class Recipients(UserId first, UserId second) : INotificationRecipientGateway
    {
        public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([]);
        public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([first, second]);
        public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([]);
        public Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([]);
    }
}
