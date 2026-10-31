using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.Modules.Caregivers.Application.Availability;

/// <summary>
/// The checkout-side availability probe. It runs the same rule function as the
/// date-specific read, so checkout can never accept a slot the read would not
/// offer.
/// </summary>
public sealed record CheckCaregiverAvailabilityQuery(
    Guid CaregiverId,
    DateOnly Date,
    BookingShiftType ShiftType,
    TimeOnly StartTime,
    TimeOnly EndTime) : IQuery<bool>;

public sealed class CheckCaregiverAvailabilityQueryHandler(
    ICaregiversDbContext caregivers,
    ICaregiverBusyBookingLookup busyLookup) : IQueryHandler<CheckCaregiverAvailabilityQuery, bool>
{
    public async Task<Result<bool>> Handle(
        CheckCaregiverAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CaregiverId == Guid.Empty ||
            request.Date == default ||
            !Enum.IsDefined(request.ShiftType))
        {
            return false;
        }

        Caregiver? caregiver = await caregivers.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            // The owning checkout keeps its existing caregiver-not-found 404
            // (pricing runs first); this seam only answers "available?".
            return false;
        }

        IReadOnlyList<CaregiverBusyWindow> busy = await busyLookup
            .GetBusyWindowsAsync(caregiver.Id, request.Date, cancellationToken);

        return CaregiverDayAvailabilityRules.Contains(
            caregiver,
            request.Date,
            request.ShiftType,
            busy,
            request.StartTime,
            request.EndTime);
    }
}
