using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

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
        var handler = new CreateCheckInAlertNotificationsCommandHandler(db, gateway);

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

        var retry = await handler.Handle(new CreateCheckInAlertNotificationsCommand(
            new ElderlyRecipient(elderlyIdentity, elderlyId), "ElderlyCheckIn", "NegativeCheckIn",
            "Daily check-in alert", "Not feeling well", DateTime.UtcNow, new DateOnly(2026, 9, 27)), CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, (await db.Notifications.ToListAsync()).Count);
    }

    private sealed class DuplicateRecipientsGateway(UserId first, UserId second) : INotificationRecipientGateway
    {
        public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([first, second, first]);

        public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([]);

        public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserId>>([]);
    }
}
