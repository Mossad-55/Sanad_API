using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public sealed record UpdateMedicalPricingCommand(
    UserId UserId,
    decimal HomeVisitPrice,
    decimal EightHourShiftPrice,
    decimal TwelveHourShiftPrice,
    decimal TwentyFourHourShiftPrice)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateMedicalPricingCommandValidator
    : AbstractValidator<UpdateMedicalPricingCommand>
{
    public UpdateMedicalPricingCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);

        RuleFor(c => c.HomeVisitPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("Home Visit price cannot have more than two decimal places.");

        RuleFor(c => c.EightHourShiftPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("8-hour shift price cannot have more than two decimal places.");

        RuleFor(c => c.TwelveHourShiftPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("12-hour shift price cannot have more than two decimal places.");

        RuleFor(c => c.TwentyFourHourShiftPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("24-hour shift price cannot have more than two decimal places.");
    }

    private static bool HaveAtMostTwoDecimals(decimal price) =>
        decimal.Round(
            price,
            decimals: 2,
            MidpointRounding.ToEven) == price;
}

public sealed class UpdateMedicalPricingCommandHandler
    : ICommandHandler<UpdateMedicalPricingCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateMedicalPricingCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateMedicalPricingCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (caregiver.Type != CaregiverType.Medical)
        {
            return OnboardingErrors.WrongCaregiverType;
        }

        caregiver.UpdateMedicalPricing(
            request.HomeVisitPrice,
            request.EightHourShiftPrice,
            request.TwelveHourShiftPrice,
            request.TwentyFourHourShiftPrice);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

public sealed record UpdateCompanionPricingCommand(
    UserId UserId,
    decimal HourlyPrice,
    decimal EightHourDayPrice,
    decimal OvernightPrice)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateCompanionPricingCommandValidator
    : AbstractValidator<UpdateCompanionPricingCommand>
{
    public UpdateCompanionPricingCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);

        RuleFor(c => c.HourlyPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("Hourly price cannot have more than two decimal places.");

        RuleFor(c => c.EightHourDayPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("8-hour day price cannot have more than two decimal places.");

        RuleFor(c => c.OvernightPrice)
            .GreaterThan(0)
            .Must(HaveAtMostTwoDecimals)
            .WithMessage("Overnight price cannot have more than two decimal places.");
    }

    private static bool HaveAtMostTwoDecimals(decimal price) =>
        decimal.Round(
            price,
            decimals: 2,
            MidpointRounding.ToEven) == price;
}

public sealed class UpdateCompanionPricingCommandHandler
    : ICommandHandler<UpdateCompanionPricingCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCompanionPricingCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateCompanionPricingCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (caregiver.Type != CaregiverType.Companion)
        {
            return OnboardingErrors.WrongCaregiverType;
        }

        caregiver.UpdateCompanionPricing(
            request.HourlyPrice,
            request.EightHourDayPrice,
            request.OvernightPrice);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}