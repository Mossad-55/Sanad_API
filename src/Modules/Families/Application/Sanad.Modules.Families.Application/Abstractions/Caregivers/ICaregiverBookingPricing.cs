using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Abstractions.Caregivers;

public sealed record CaregiverBookingPrice(
    BookingCaregiverType CaregiverType,
    decimal BaseFee);

public interface ICaregiverBookingPricing
{
    Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
        CaregiverId caregiverId,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        CancellationToken cancellationToken = default);
}