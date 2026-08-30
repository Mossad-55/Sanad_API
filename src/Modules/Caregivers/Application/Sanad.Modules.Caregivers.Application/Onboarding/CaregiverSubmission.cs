using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public sealed record SubmitCaregiverCommand(
    UserId UserId,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand<CaregiverProfileResponse>;

public sealed class SubmitCaregiverCommandValidator
    : AbstractValidator<SubmitCaregiverCommand>
{
    public SubmitCaregiverCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class SubmitCaregiverCommandHandler
    : ICommandHandler<SubmitCaregiverCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SubmitCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        SubmitCaregiverCommand request,
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

        try
        {
            // Submit from Onboarding, resubmit after admin-requested
            // corrections. Domain enforces both the transition and the
            // full readiness checklist (profile, selections, pricing,
            // schedule, mandatory medical certificates).
            if (caregiver.Status ==
                CaregiverStatus.Onboarding)
            {
                caregiver.SubmitForReview(
                    request.UtcNow,
                    request.CurrentDate);
            }
            else if (caregiver.Status ==
                     CaregiverStatus.NeedsCorrection)
            {
                caregiver.ResubmitForReview(
                    request.UtcNow,
                    request.CurrentDate);
            }
            else
            {
                return OnboardingErrors.InvalidState;
            }
        }
        catch (DomainException)
        {
            // Readiness failure (missing section or expired/pending
            // mandatory certificate) or an invalid transition.
            return OnboardingErrors.InvalidState;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}