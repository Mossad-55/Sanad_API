using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Notifications;

public sealed class CheckInAlertNotificationCommandTests
{
    [Fact]
    public async Task Handler_DeduplicatesEligibleRecipients_AndCreatesNegativeCheckInAlerts()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var first = UserId.New();
        var second = UserId.New();
        var elderlyIdentity = UserId.New();
        var elderlyId = Guid.NewGuid();
        var gateway = new DuplicateRecipientsGateway(first, second);
        var languages = new FixedNotificationLanguageGateway(new Dictionary<Guid, UiLanguage>
        {
            [first.Value] = UiLanguage.Arabic,
            [second.Value] = UiLanguage.English
        });
        var handler = new CreateCheckInAlertNotificationsCommandHandler(db, gateway, languages);

        var result = await handler.Handle(new CreateCheckInAlertNotificationsCommand(
            new ElderlyRecipient(elderlyIdentity, elderlyId), "ElderlyCheckIn", "NegativeCheckIn",
            "Daily check-in alert", "Not feeling well", DateTime.UtcNow, new DateOnly(2026, 9, 27)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var rows = await db.Notifications.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { first.Value, second.Value }.OrderBy(x => x), rows.Select(x => x.RecipientUserId).OrderBy(x => x));
        Assert.All(rows, row =>
        {
            Assert.Equal("ElderlyCheckIn", row.Category);
            Assert.Equal("NegativeCheckIn", row.Type);
            Assert.Equal("Elderly", row.DestinationEntityKind);
        });

        // F8: one batch lookup answers the whole fan-out, and every row carries
        // the catalog copy for its own recipient's persisted language instead of
        // the English-only pair the command supplied.
        // Exactly one batch lookup for the whole fan-out (not one per recipient);
        // the idempotent replay is exercised further below.
        Assert.Equal(1, languages.LookupCount);
        Assert.Equal(new[] { first.Value, second.Value }.OrderBy(x => x), languages.RequestedIds.OrderBy(x => x));
        var arabicRow = Assert.Single(rows, x => x.RecipientUserId == first.Value);
        var englishRow = Assert.Single(rows, x => x.RecipientUserId == second.Value);
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.Arabic).Title, arabicRow.Title);
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.Arabic).Body, arabicRow.Body);
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.English).Title, englishRow.Title);
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.English).Body, englishRow.Body);
        Assert.DoesNotContain("Daily check-in alert", arabicRow.Title + arabicRow.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Not feeling well", arabicRow.Title + arabicRow.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(elderlyId.ToString(), arabicRow.Title + arabicRow.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(elderlyId.ToString("N"), englishRow.Title + englishRow.Body, StringComparison.OrdinalIgnoreCase);

        var retry = await handler.Handle(new CreateCheckInAlertNotificationsCommand(
            new ElderlyRecipient(elderlyIdentity, elderlyId), "ElderlyCheckIn", "NegativeCheckIn",
            "Daily check-in alert", "Not feeling well", DateTime.UtcNow, new DateOnly(2026, 9, 27)), CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, (await db.Notifications.ToListAsync()).Count);
    }

    [Fact]
    public async Task Handler_keeps_the_supplied_copy_for_a_pair_the_catalog_does_not_own()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recipient = UserId.New();
        var handler = new CreateCheckInAlertNotificationsCommandHandler(db,
            new DuplicateRecipientsGateway(recipient, recipient),
            FixedNotificationLanguageGateway.ArabicDefault);

        var result = await handler.Handle(new CreateCheckInAlertNotificationsCommand(
            new ElderlyRecipient(UserId.New(), Guid.NewGuid()), "ElderlyCheckIn", "PositiveCheckIn",
            "Daily check-in received", "Feeling well today", DateTime.UtcNow, new DateOnly(2026, 9, 28)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        var row = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal("Daily check-in received", row.Title);
        Assert.Equal("Feeling well today", row.Body);
    }

    private sealed class DuplicateRecipientsGateway(UserId first, UserId second) : INotificationRecipientGateway
    {
        public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([first, second, first]);

        public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([]);

        public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([]);

        public Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([]);
    }
}
