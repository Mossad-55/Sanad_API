using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

// --------------------------- Medical schedule -------------------------

public sealed record MedicalShiftItem(
    DayOfWeek DayOfWeek,
    MedicalShiftType ShiftType);

public sealed record MedicalHomeVisitWindowItem(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record UpdateMedicalScheduleCommand(
    UserId UserId,
    IReadOnlyList<MedicalShiftItem> Shifts,
    IReadOnlyList<MedicalHomeVisitWindowItem> HomeVisitWindows)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateMedicalScheduleCommandValidator
    : AbstractValidator<UpdateMedicalScheduleCommand>
{
    public UpdateMedicalScheduleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleForEach(c => c.Shifts)
            .ChildRules(shift =>
            {
                shift.RuleFor(s => s.DayOfWeek).IsInEnum();
                shift.RuleFor(s => s.ShiftType).IsInEnum();
            });
        RuleForEach(c => c.HomeVisitWindows)
            .ChildRules(window =>
            {
                window.RuleFor(w => w.DayOfWeek).IsInEnum();
                window.RuleFor(w => w.EndTime)
                    .GreaterThan(w => w.StartTime)
                    .WithMessage(
                        "Home visit window must end after it starts.");
            });
    }
}

public sealed class UpdateMedicalScheduleCommandHandler
    : ICommandHandler<UpdateMedicalScheduleCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateMedicalScheduleCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateMedicalScheduleCommand request,
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

        IReadOnlyCollection<MedicalShiftInput> shifts =
            request.Shifts?
                .Select(item =>
                    new MedicalShiftInput(
                        item.DayOfWeek,
                        item.ShiftType))
                .ToList()
            ?? [];

        IReadOnlyCollection<MedicalHomeVisitWindowInput> windows =
            request.HomeVisitWindows?
                .Select(item =>
                    new MedicalHomeVisitWindowInput(
                        item.DayOfWeek,
                        item.StartTime,
                        item.EndTime))
                .ToList()
            ?? [];

        try
        {
            caregiver.ReplaceMedicalSchedule(
                shifts,
                windows);
        }
        catch (DomainException)
        {
            // Overlap, duplicate-day shift, shift/window day mix, or
            // an Active caregiver clearing the whole schedule.
            return OnboardingErrors.InvalidSchedule;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

// -------------------------- Companion schedule ------------------------

public sealed record CompanionAvailabilityWindowItem(
    CompanionBookingType BookingType,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record UpdateCompanionScheduleCommand(
    UserId UserId,
    IReadOnlyList<CompanionAvailabilityWindowItem> Windows)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateCompanionScheduleCommandValidator
    : AbstractValidator<UpdateCompanionScheduleCommand>
{
    public UpdateCompanionScheduleCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleForEach(c => c.Windows)
            .ChildRules(window =>
            {
                window.RuleFor(w => w.DayOfWeek).IsInEnum();
                window.RuleFor(w => w.BookingType).IsInEnum();
                window.RuleFor(w => w.EndTime)
                    .GreaterThan(w => w.StartTime)
                    .WithMessage(
                        "Availability window must end after it starts.");
            });
    }
}

public sealed class UpdateCompanionScheduleCommandHandler
    : ICommandHandler<UpdateCompanionScheduleCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCompanionScheduleCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateCompanionScheduleCommand request,
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

        IReadOnlyCollection<CompanionAvailabilityWindowInput> windows =
            request.Windows?
                .Select(item =>
                    new CompanionAvailabilityWindowInput(
                        item.BookingType,
                        item.DayOfWeek,
                        item.StartTime,
                        item.EndTime))
                .ToList()
            ?? [];

        try
        {
            caregiver.ReplaceCompanionSchedule(windows);
        }
        catch (DomainException)
        {
            // Overlap or an Active caregiver clearing all windows.
            return OnboardingErrors.InvalidSchedule;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

// ---------------------------- Availability ----------------------------

public sealed record BecomeAvailableCommand(
    UserId UserId,
    DateOnly CurrentDate)
    : ICommand<CaregiverProfileResponse>;

public sealed class BecomeAvailableCommandHandler
    : ICommandHandler<BecomeAvailableCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public BecomeAvailableCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        BecomeAvailableCommand request,
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

        if (caregiver.Status != CaregiverStatus.Active)
        {
            return OnboardingErrors.NotActive;
        }

        try
        {
            // Active medical caregiver additionally needs mandatory
            // certificates verified and unexpired (current date).
            caregiver.BecomeAvailable(request.CurrentDate);
        }
        catch (DomainException)
        {
            return OnboardingErrors.NotActive;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

public sealed record BecomeUnavailableCommand(
    UserId UserId)
    : ICommand<CaregiverProfileResponse>;

public sealed class BecomeUnavailableCommandHandler
    : ICommandHandler<BecomeUnavailableCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public BecomeUnavailableCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        BecomeUnavailableCommand request,
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

        caregiver.BecomeUnavailable();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}