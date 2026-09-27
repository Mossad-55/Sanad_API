namespace Sanad.Modules.Families.Application.Abstractions.Notifications;

public sealed record ElderlyCheckInAlertRequest(Guid ElderlyIdentityUserId, Guid ElderlyEntityId, DateOnly LocalDate);

public interface IElderlyCheckInAlertGateway
{
    Task<int> CreateNegativeCheckInAlertsAsync(ElderlyCheckInAlertRequest elderly, DateTime createdOnUtc, CancellationToken cancellationToken = default);
}
