using MediatR;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Caregivers;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;

namespace Sanad.API.CaregiversIntegration;

/// <summary>
/// Adapter for the Identity module's caregiver-account port (SET-8d). Maps the
/// Caregivers read model onto CaregiverAccountInfo and routes the D11 booking
/// guard to the Families module, so Identity never references either module.
/// </summary>
public sealed class CaregiverAccountGateway : ICaregiverAccountGateway
{
    private readonly ISender _sender;

    public CaregiverAccountGateway(ISender sender)
    {
        _sender = sender;
    }

    public async Task<CaregiverAccountInfo?> GetCaregiverAccountAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        Result<CaregiverAccountSnapshot> result = await _sender.Send(
            new GetCaregiverAccountByUserIdQuery(userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return null;
        }

        return new CaregiverAccountInfo(
            result.Value.CaregiverId,
            result.Value.IsDeactivated);
    }

    public async Task<Result<bool>> HasActiveBookingsAsync(
        CaregiverId caregiverId,
        CancellationToken cancellationToken = default)
    {
        return await _sender.Send(
            new HasActiveCaregiverBookingsQuery(caregiverId),
            cancellationToken);
    }

    public async Task<Result> DeactivateCaregiverAsync(
        CaregiverId caregiverId,
        string reason,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return await _sender.Send(
            new DeactivateCaregiverCommand(
                caregiverId,
                reason,
                utcNow),
            cancellationToken);
    }
}
