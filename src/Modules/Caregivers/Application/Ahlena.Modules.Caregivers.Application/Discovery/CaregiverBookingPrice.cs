using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Finance.Application;

namespace Ahlena.Modules.Caregivers.Application.Discovery;

public sealed record CaregiverBookingPriceResult(
    int CaregiverType,
    decimal BaseFee,
    decimal PlatformFeeRatePercentage,
    decimal PlatformFeeAmount,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    int PlatformChargeRuleVersion,
    decimal TotalPayable);

public sealed record GetCaregiverBookingPriceQuery(
    CaregiverId CaregiverId,
    BookingShiftType ShiftType,
    TimeOnly StartTime,
    TimeOnly EndTime) : IQuery<CaregiverBookingPriceResult>;

public sealed class GetCaregiverBookingPriceQueryHandler
    : IQueryHandler<GetCaregiverBookingPriceQuery, CaregiverBookingPriceResult>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IPlatformChargeRuleReader _chargeRules;

    public GetCaregiverBookingPriceQueryHandler(ICaregiversDbContext dbContext, IPlatformChargeRuleReader chargeRules)
    {
        _dbContext = dbContext;
        _chargeRules = chargeRules;
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
            var charge = await _chargeRules.GetEffectiveAsync(DateTime.UtcNow, cancellationToken);
            if (charge is null) throw new DomainException("Shared platform fee and tax configuration is not available.");
            BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
                caregiver,
                request.ShiftType,
                request.StartTime,
                request.EndTime,
                charge.PlatformFeeRatePercentage,
                charge.TaxRatePercentage,
                charge.Version);

            return Result<CaregiverBookingPriceResult>.Success(
                new CaregiverBookingPriceResult(
                    (int)caregiver.Type,
                    snapshot.BaseCaregiverFee, snapshot.PlatformFeePercentage, snapshot.PlatformFeeAmount,
                    snapshot.TaxRatePercentage, snapshot.TaxAmount, snapshot.PlatformChargeRuleVersion!.Value,
                    snapshot.TotalPayableAmount));
        }
        catch (DomainException exception)
        {
            return Result<CaregiverBookingPriceResult>.Failure(
                new Error("Caregivers.Discovery.QuoteNotAvailable", exception.Message));
        }
    }
}
