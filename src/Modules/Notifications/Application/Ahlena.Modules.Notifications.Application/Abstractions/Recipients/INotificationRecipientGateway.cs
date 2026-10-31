using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Notifications.Application.Abstractions.Recipients;

public interface INotificationRecipientGateway
{
    Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(
        ElderlyRecipient elderly,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default);
}

public sealed record ElderlyRecipient(UserId ElderlyIdentityUserId, Guid ElderlyEntityId);
