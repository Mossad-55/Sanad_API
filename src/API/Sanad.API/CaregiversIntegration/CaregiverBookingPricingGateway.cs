using MediatR;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.CaregiversIntegration;

public sealed class CaregiverBookingPricingGateway : ICaregiverBookingPricing
{
    private readonly ISender _sender;

    public CaregiverBookingPricingGateway(ISender sender)
    {
        _sender = sender;
    }

    public async Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
        CaregiverId caregiverId,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        CancellationToken cancellationToken = default)
    {
        Result<CaregiverBookingPriceResult> result = await _sender.Send(
            new GetCaregiverBookingPriceQuery(caregiverId, shiftType, startTime, endTime),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Result<CaregiverBookingPrice>.Failure(result.Error);
        }

        var caregiverType = (BookingCaregiverType)result.Value.CaregiverType;

        if (!Enum.IsDefined(caregiverType))
        {
            return Result<CaregiverBookingPrice>.Failure(
                new Error("Caregivers.Discovery.QuoteNotAvailable", "Caregiver pricing is not available."));
        }

        return Result<CaregiverBookingPrice>.Success(
            new CaregiverBookingPrice(caregiverType, result.Value.BaseFee));
    }
}