using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class MedicationLateAlertNotificationTests
{
    [Fact]
    public async Task FanOut_UsesMedicationRemindersRecipientsAndIsIdempotentPerDoseRecipient()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recipient = UserId.New();
        // F8: the copy follows the recipient's persisted language, read in one batch.
        var languages = FixedNotificationLanguageGateway.For(recipient.Value, UiLanguage.English);
        var handler = new CreateMedicationLateAlertNotificationsCommandHandler(db, new Recipients(recipient), languages);
        var medication = Guid.NewGuid();
        var elderly = new ElderlyRecipient(UserId.New(), Guid.NewGuid());
        var request = new CreateMedicationLateAlertNotificationsCommand(elderly, medication,
            new DateOnly(2026, 9, 10), new TimeOnly(8, 0), DateTime.UtcNow);

        var first = await handler.Handle(request, default);
        var replay = await handler.Handle(request, default);

        Assert.Equal(1, first.Value);
        Assert.Equal(0, replay.Value);
        var notification = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(recipient.Value, notification.RecipientUserId);
        Assert.Equal("MedicationReminders", notification.Category);
        Assert.Equal("MedicationDoseMissed", notification.Type);

        // F8: English recipient reads English copy in one batch lookup, the
        // structured destination keeps the medication id, and the generated
        // copy never repeats an identifier.
        // One batch per invocation: the first call and the idempotent replay
        // each resolve copy once, never once per recipient.
        Assert.Equal(2, languages.LookupCount);
        Assert.Equal([recipient.Value], languages.RequestedIds);
        Assert.Equal(NotificationTextCatalog.MedicationDoseMissed(UiLanguage.English).Title, notification.Title);
        Assert.Equal(NotificationTextCatalog.MedicationDoseMissed(UiLanguage.English).Body, notification.Body);
        Assert.Equal("Medication", notification.DestinationEntityKind);
        Assert.Equal(medication, notification.DestinationEntityId);
        Assert.DoesNotContain(medication.ToString(), notification.Title + notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(medication.ToString("N"), notification.Title + notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Recipients(UserId recipient) : INotificationRecipientGateway
    {
        public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserId>>([recipient]);
        public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
