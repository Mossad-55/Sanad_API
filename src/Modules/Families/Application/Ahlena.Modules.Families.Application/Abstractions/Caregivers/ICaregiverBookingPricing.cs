using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.Bookings;

namespace Ahlena.Modules.Families.Application.Abstractions.Caregivers;

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