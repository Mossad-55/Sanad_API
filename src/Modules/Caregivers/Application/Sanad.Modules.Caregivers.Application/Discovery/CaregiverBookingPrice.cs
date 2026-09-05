using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record CaregiverBookingPriceResult(
    int CaregiverType,
    decimal BaseFee);

public sealed record GetCaregiverBookingPriceQuery(
    CaregiverId CaregiverId,
    BookingShiftType ShiftType,
    TimeOnly StartTime,
    TimeOnly EndTime) : IQuery<CaregiverBookingPriceResult>;

public sealed class GetCaregiverBookingPriceQueryHandler
    : IQueryHandler<GetCaregiverBookingPriceQuery, CaregiverBookingPriceResult>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverBookingPriceQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverBookingPriceResult>> Handle(
        GetCaregiverBookingPriceQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await _dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CaregiverId, cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverBookingPriceResult>.Failure(
                new Error("Caregivers.Discovery.CaregiverNotFound", "Caregiver was not found."));
        }

        try
        {
            BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
                caregiver,
                request.ShiftType,
                request.StartTime,
                request.EndTime);

            return Result<CaregiverBookingPriceResult>.Success(
                new CaregiverBookingPriceResult(
                    (int)caregiver.Type,
                    snapshot.BaseCaregiverFee));
        }
        catch (DomainException exception)
        {
            return Result<CaregiverBookingPriceResult>.Failure(
                new Error("Caregivers.Discovery.QuoteNotAvailable", exception.Message));
        }
    }
}