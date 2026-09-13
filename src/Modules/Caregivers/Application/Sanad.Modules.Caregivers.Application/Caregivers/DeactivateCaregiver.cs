using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Caregivers;

public sealed record DeactivateCaregiverCommand(
    CaregiverId CaregiverId,
    string Reason,
    DateTime UtcNow)
    : ICommand;

public sealed class DeactivateCaregiverCommandValidator
    : AbstractValidator<DeactivateCaregiverCommand>
{
    public DeactivateCaregiverCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>
/// SET-8d — moves a caregiver profile to the terminal Deactivated state.
/// Anonymize & retain: the row and its history stay, the caregiver simply
/// disappears from discovery and can no longer be booked.
/// </summary>
public sealed class DeactivateCaregiverCommandHandler
    : ICommandHandler<DeactivateCaregiverCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public DeactivateCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        DeactivateCaregiverCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await _dbContext.Caregivers
            .SingleOrDefaultAsync(
                c => c.Id == request.CaregiverId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                DeactivationErrors.CaregiverNotFound);
        }

        caregiver.Deactivate(
            request.Reason,
            request.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public static class DeactivationErrors
{
    public static readonly Error CaregiverNotFound =
        new(
            "Caregivers.Deactivation.CaregiverNotFound",
            "The caregiver profile was not found.");
}
