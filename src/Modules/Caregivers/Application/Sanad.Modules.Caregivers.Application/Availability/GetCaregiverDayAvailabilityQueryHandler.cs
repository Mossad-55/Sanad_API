using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Availability;

public sealed class GetCaregiverDayAvailabilityQueryHandler(
    ICaregiversDbContext caregivers,
    ICaregiverBusyBookingLookup busyLookup) : IQueryHandler<
    GetCaregiverDayAvailabilityQuery,
    CaregiverDayAvailabilityResponse>
{
    public async Task<Result<CaregiverDayAvailabilityResponse>> Handle(
        GetCaregiverDayAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CaregiverId == Guid.Empty ||
            request.Date == default ||
            !Enum.IsDefined(request.ShiftType))
        {
            return CaregiverAvailabilityErrors.InvalidQuery;
        }

        Caregiver? caregiver = await caregivers.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return CaregiverAvailabilityErrors.NotFound;
        }

        IReadOnlyList<CaregiverBusyWindow> busy = await busyLookup
            .GetBusyWindowsAsync(caregiver.Id, request.Date, cancellationToken);

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                request.Date,
                request.ShiftType,
                busy);

        return new CaregiverDayAvailabilityResponse(
            caregiver.Id.Value,
            request.Date,
            CaregiverAvailabilityTimeZone.Id,
            request.ShiftType,
            intervals);
    }
}

public static class CaregiverAvailabilityErrors
{
    public static readonly Error InvalidQuery = new(
        "Caregivers.Availability.InvalidQuery",
        "The availability query requires a valid date and shift type.");

    public static readonly Error NotFound = new(
        "Caregivers.NotFound",
        "Caregiver not found.");
}
