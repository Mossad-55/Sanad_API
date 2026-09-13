using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Onboarding;

namespace Sanad.Modules.Caregivers.Application.Privacy;

public sealed record CaregiverPrivacyResponse(
    bool ShowProfile,
    bool ShowRating,
    bool ShowPhone,
    bool ShareLocation);

public sealed record GetCaregiverPrivacyQuery(
    UserId UserId)
    : IQuery<CaregiverPrivacyResponse>;

public sealed class GetCaregiverPrivacyQueryValidator
    : AbstractValidator<GetCaregiverPrivacyQuery>
{
    public GetCaregiverPrivacyQueryValidator()
    {
        RuleFor(q => q.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class GetCaregiverPrivacyQueryHandler
    : IQueryHandler<GetCaregiverPrivacyQuery, CaregiverPrivacyResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverPrivacyQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverPrivacyResponse>> Handle(
        GetCaregiverPrivacyQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await _dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.UserId == request.UserId,
                cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        var visibility = caregiver.Visibility;

        // Handle null for backward compatibility (pre-migration rows)
        if (visibility is null)
        {
            return Result<CaregiverPrivacyResponse>.Success(
                new CaregiverPrivacyResponse(
                    ShowProfile: true,
                    ShowRating: true,
                    ShowPhone: false,
                    ShareLocation: false));
        }

        return Result<CaregiverPrivacyResponse>.Success(
            new CaregiverPrivacyResponse(
                visibility.ShowProfile,
                visibility.ShowRating,
                visibility.ShowPhone,
                visibility.ShareLocation));
    }
}

public sealed record UpdateCaregiverPrivacyCommand(
    UserId UserId,
    bool ShowProfile,
    bool ShowRating,
    bool ShowPhone,
    bool ShareLocation)
    : ICommand;

public sealed class UpdateCaregiverPrivacyCommandValidator
    : AbstractValidator<UpdateCaregiverPrivacyCommand>
{
    public UpdateCaregiverPrivacyCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
    }
}

public sealed class UpdateCaregiverPrivacyCommandHandler
    : ICommandHandler<UpdateCaregiverPrivacyCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCaregiverPrivacyCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        UpdateCaregiverPrivacyCommand request,
        CancellationToken cancellationToken)
    {
        var caregiver = await _dbContext.Caregivers
            .SingleOrDefaultAsync(
                c => c.UserId == request.UserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(OnboardingErrors.NotFound);
        }

        caregiver.UpdateVisibility(
            request.ShowProfile,
            request.ShowRating,
            request.ShowPhone,
            request.ShareLocation);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
