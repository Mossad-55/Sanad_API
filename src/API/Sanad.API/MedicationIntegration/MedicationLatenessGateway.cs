using MediatR;
using Sanad.Modules.Cms.Application.MedicationLateness;
using Sanad.Modules.Families.Application.Abstractions.Medications;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.MedicationIntegration;

public sealed class MedicationLatenessSettingGateway(ISender sender) : IMedicationLatenessSettingGateway
{
    public async Task<MedicationLatenessSettingSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetMedicationLatenessSettingQuery(), cancellationToken);
        return result.IsSuccess ? new(result.Value.ThresholdMinutes, result.Value.Version) : null;
    }
}

public sealed class MedicationLateAlertGateway(ISender sender) : IMedicationLateAlertGateway
{
    public async Task<int> CreateAsync(MedicationLateAlertRequest request, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new CreateMedicationLateAlertNotificationsCommand(new ElderlyRecipient(new(request.ElderlyIdentityUserId.Value), request.ElderlyId), request.MedicationId, request.ScheduledDate, request.ScheduledTime, request.CreatedOnUtc), cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
        return result.Value;
    }
}
