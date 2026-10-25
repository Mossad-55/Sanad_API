using MediatR;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Availability;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;

namespace Sanad.API.CaregiversIntegration;

/// <summary>
/// Checkout's availability gate. It sends the same query the
/// <c>availability/day</c> read uses, so both surfaces call one rule function;
/// an unavailable seam answer is a proven slot rejection, not an unknown
/// outcome.
/// </summary>
public sealed class CaregiverAvailabilityRulesGateway(
    ISender sender) : ICaregiverAvailabilityRules
{
    public async Task<bool> IsAvailableAsync(
        CaregiverId caregiverId,
        DateOnly date,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new CheckCaregiverAvailabilityQuery(
                caregiverId.Value,
                date,
                shiftType,
                startTime,
                endTime),
            cancellationToken);

        return result.IsSuccess && result.Value;
    }
}
