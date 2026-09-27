using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class HelpRequestNotificationCommandTests
{
    [Fact]
    public async Task Handler_PersistsDurableNotificationsWithStableIdempotencyKeys()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var first = UserId.New(); var second = UserId.New(); var requestId = Guid.NewGuid();
        var handler = new CreateHelpRequestAlertNotificationsCommandHandler(db, new Recipients(first, second));
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
    }

    private sealed class Recipients(UserId first, UserId second) : INotificationRecipientGateway
    {
        public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([]);
        public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([first, second]);
        public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserId>>([]);
    }
}
