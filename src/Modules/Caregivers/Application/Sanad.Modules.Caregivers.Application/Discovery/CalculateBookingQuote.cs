using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record BookingQuoteResponse(
    Guid CaregiverId,
    CaregiverType CaregiverType,
    BookingShiftType ShiftType,
    decimal BaseCaregiverFee,
    decimal PlatformFeePercentage,
    decimal PlatformFeeAmount,
    decimal TaxRatePercentage,
    decimal TaxAmount,
    int PlatformChargeRuleVersion,
    decimal TotalPayableAmount,
    string Currency);

public sealed record CalculateBookingQuoteQuery(
    CaregiverId CaregiverId,
    BookingShiftType ShiftType,
    TimeOnly StartTime,
    TimeOnly EndTime) : IQuery<BookingQuoteResponse>;

public sealed class CalculateBookingQuoteQueryValidator : AbstractValidator<CalculateBookingQuoteQuery>
{
    public CalculateBookingQuoteQueryValidator()
    {
        RuleFor(q => q.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(q => q.ShiftType).IsInEnum();
    }
}

public sealed class CalculateBookingQuoteQueryHandler : IQueryHandler<CalculateBookingQuoteQuery, BookingQuoteResponse>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IPlatformChargeRuleReader _chargeRules;

    public CalculateBookingQuoteQueryHandler(ICaregiversDbContext dbContext, IPlatformChargeRuleReader chargeRules)
    {
        _dbContext = dbContext;
        _chargeRules = chargeRules;
    }

    public async Task<Result<BookingQuoteResponse>> Handle(
        CalculateBookingQuoteQuery request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await _dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == request.CaregiverId && c.Status == CaregiverStatus.Active,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<BookingQuoteResponse>.Failure(
                new Error("Caregivers.Discovery.CaregiverNotFound", "Caregiver not found or is currently inactive."));
        }

        try
        {
            var charge = await _chargeRules.GetEffectiveAsync(DateTime.UtcNow, cancellationToken);
            if (charge is null) throw new InvalidOperationException("Shared platform fee and tax configuration is not available.");
            BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
                caregiver,
                request.ShiftType,
                request.StartTime,
                request.EndTime,
                charge.PlatformFeeRatePercentage,
                charge.TaxRatePercentage,
                charge.Version);

            var response = new BookingQuoteResponse(
                caregiver.Id.Value,
                caregiver.Type,
                request.ShiftType,
                snapshot.BaseCaregiverFee,
                snapshot.PlatformFeePercentage,
                snapshot.PlatformFeeAmount,
                snapshot.TaxRatePercentage,
                snapshot.TaxAmount,
                snapshot.PlatformChargeRuleVersion!.Value,
                snapshot.TotalPayableAmount,
                snapshot.Currency);

            return Result<BookingQuoteResponse>.Success(response);
        }
        catch (Exception ex)
        {
            return Result<BookingQuoteResponse>.Failure(
                new Error("Caregivers.Pricing.CalculationFailed", ex.Message));
        }
    }
}
