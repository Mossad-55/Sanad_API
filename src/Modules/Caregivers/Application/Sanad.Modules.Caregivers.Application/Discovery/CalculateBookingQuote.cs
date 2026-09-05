using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record BookingQuoteResponse(
    Guid CaregiverId,
    CaregiverType CaregiverType,
    BookingShiftType ShiftType,
    decimal BaseCaregiverFee,
    decimal PlatformFeePercentage,
    decimal PlatformFeeAmount,
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

    public CalculateBookingQuoteQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
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
                new Error("Caregivers.Pricing.CaregiverNotFound", "Caregiver not found or is currently inactive."));
        }

        try
        {
            BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
                caregiver,
                request.ShiftType,
                request.StartTime,
                request.EndTime);

            var response = new BookingQuoteResponse(
                caregiver.Id.Value,
                caregiver.Type,
                request.ShiftType,
                snapshot.BaseCaregiverFee,
                snapshot.PlatformFeePercentage,
                snapshot.PlatformFeeAmount,
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