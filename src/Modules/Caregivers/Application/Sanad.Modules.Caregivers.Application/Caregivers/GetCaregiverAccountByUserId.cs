using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Caregivers;

public sealed record CaregiverAccountSnapshot(
    CaregiverId CaregiverId,
    bool IsDeactivated);

/// <summary>
/// SET-8d read model used by the API caregiver-account gateway. The Identity
/// module never references this snapshot: the gateway maps it onto its own
/// CaregiverAccountInfo port record.
/// </summary>
public sealed record GetCaregiverAccountByUserIdQuery(
    UserId UserId)
    : IQuery<CaregiverAccountSnapshot>;

public sealed class GetCaregiverAccountByUserIdQueryValidator
    : AbstractValidator<GetCaregiverAccountByUserIdQuery>
{
    public GetCaregiverAccountByUserIdQueryValidator()
    {
        RuleFor(q => q.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class GetCaregiverAccountByUserIdQueryHandler
    : IQueryHandler<GetCaregiverAccountByUserIdQuery, CaregiverAccountSnapshot>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverAccountByUserIdQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverAccountSnapshot>> Handle(
        GetCaregiverAccountByUserIdQuery request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await _dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.UserId == request.UserId,
                cancellationToken);

        if (caregiver is null)
        {
            return DeactivationErrors.CaregiverNotFound;
        }

        return new CaregiverAccountSnapshot(
            caregiver.Id,
            caregiver.Status == CaregiverStatus.Deactivated);
    }
}
