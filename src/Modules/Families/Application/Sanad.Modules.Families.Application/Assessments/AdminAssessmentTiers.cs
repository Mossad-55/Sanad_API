using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.Modules.Families.Application.Assessments;

// --------------------------------- DTOs ---------------------------------

public sealed record AdminAssessmentTierResponse(
    AssessmentTierId Id,
    int ScreenOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSubtitle,
    string EnglishSubtitle,
    string BackgroundColor,
    string ArabicButtonText,
    string EnglishButtonText,
    string ImagePath,
    int MinScore,
    int MaxScore,
    bool IsActive,
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

internal static class AssessmentTierMappings
{
    public static AdminAssessmentTierResponse ToAdminResponse(
        this AssessmentTier tier) =>
        new(
            tier.Id,
            tier.ScreenOrder,
            tier.ArabicTitle,
            tier.EnglishTitle,
            tier.ArabicSubtitle,
            tier.EnglishSubtitle,
            tier.BackgroundColor,
            tier.ArabicButtonText,
            tier.EnglishButtonText,
            tier.ImagePath,
            tier.MinScore,
            tier.MaxScore,
            tier.IsActive,
            tier.ArabicRecommendations,
            tier.EnglishRecommendations,
            tier.CreatedOnUtc,
            tier.UpdatedOnUtc);
}

// -------------------------------- Create --------------------------------

public sealed record CreateAssessmentTierCommand(
    int ScreenOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSubtitle,
    string EnglishSubtitle,
    string BackgroundColor,
    string ArabicButtonText,
    string EnglishButtonText,
    string ImagePath,
    int MinScore,
    int MaxScore,
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations,
    bool IsActive)
    : ICommand<AdminAssessmentTierResponse>;

public sealed class CreateAssessmentTierCommandValidator
    : AbstractValidator<CreateAssessmentTierCommand>
{
    public CreateAssessmentTierCommandValidator()
    {
        RuleFor(c => c.ScreenOrder).GreaterThan(0);
        RuleFor(c => c.ArabicTitle).NotEmpty().MaximumLength(AssessmentTier.MaximumTitleLength);
        RuleFor(c => c.EnglishTitle).NotEmpty().MaximumLength(AssessmentTier.MaximumTitleLength);
        RuleFor(c => c.ArabicSubtitle).NotEmpty().MaximumLength(AssessmentTier.MaximumSubtitleLength);
        RuleFor(c => c.EnglishSubtitle).NotEmpty().MaximumLength(AssessmentTier.MaximumSubtitleLength);
        RuleFor(c => c.BackgroundColor).NotEmpty().MaximumLength(AssessmentTier.MaximumColorLength);
        RuleFor(c => c.ArabicButtonText).NotEmpty().MaximumLength(AssessmentTier.MaximumButtonTextLength);
        RuleFor(c => c.EnglishButtonText).NotEmpty().MaximumLength(AssessmentTier.MaximumButtonTextLength);
        RuleFor(c => c.ImagePath).NotEmpty().MaximumLength(AssessmentTier.MaximumImagePathLength);
        RuleFor(c => c.MinScore).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MaxScore).GreaterThanOrEqualTo(c => c.MinScore);
    }
}

public sealed class CreateAssessmentTierCommandHandler
    : ICommandHandler<CreateAssessmentTierCommand, AdminAssessmentTierResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public CreateAssessmentTierCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentTierResponse>> Handle(
        CreateAssessmentTierCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentTier tier;
        try
        {
            tier = AssessmentTier.Create(
                request.ScreenOrder,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSubtitle,
                request.EnglishSubtitle,
                request.BackgroundColor,
                request.ArabicButtonText,
                request.EnglishButtonText,
                request.ImagePath,
                request.MinScore,
                request.MaxScore,
                request.ArabicRecommendations,
                request.EnglishRecommendations,
                request.IsActive);
        }
        catch (DomainException)
        {
            return AssessmentErrors.InvalidTier;
        }

        _dbContext.AssessmentTiers.Add(tier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return tier.ToAdminResponse();
    }
}

// -------------------------------- Update --------------------------------

public sealed record UpdateAssessmentTierCommand(
    AssessmentTierId Id,
    int ScreenOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSubtitle,
    string EnglishSubtitle,
    string BackgroundColor,
    string ArabicButtonText,
    string EnglishButtonText,
    string? ImagePath,
    int MinScore,
    int MaxScore,
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations)
    : ICommand<AdminAssessmentTierResponse>;

public sealed class UpdateAssessmentTierCommandValidator
    : AbstractValidator<UpdateAssessmentTierCommand>
{
    public UpdateAssessmentTierCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AssessmentTierId.Empty);
        RuleFor(c => c.ScreenOrder).GreaterThan(0);
        RuleFor(c => c.ArabicTitle).NotEmpty().MaximumLength(AssessmentTier.MaximumTitleLength);
        RuleFor(c => c.EnglishTitle).NotEmpty().MaximumLength(AssessmentTier.MaximumTitleLength);
        RuleFor(c => c.ArabicSubtitle).NotEmpty().MaximumLength(AssessmentTier.MaximumSubtitleLength);
        RuleFor(c => c.EnglishSubtitle).NotEmpty().MaximumLength(AssessmentTier.MaximumSubtitleLength);
        RuleFor(c => c.BackgroundColor).NotEmpty().MaximumLength(AssessmentTier.MaximumColorLength);
        RuleFor(c => c.ArabicButtonText).NotEmpty().MaximumLength(AssessmentTier.MaximumButtonTextLength);
        RuleFor(c => c.EnglishButtonText).NotEmpty().MaximumLength(AssessmentTier.MaximumButtonTextLength);
        RuleFor(c => c.ImagePath)
            .MaximumLength(AssessmentTier.MaximumImagePathLength)
            .When(c => c.ImagePath is not null);
        RuleFor(c => c.MinScore).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MaxScore).GreaterThanOrEqualTo(c => c.MinScore);
    }
}

public sealed class UpdateAssessmentTierCommandHandler
    : ICommandHandler<UpdateAssessmentTierCommand, AdminAssessmentTierResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateAssessmentTierCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentTierResponse>> Handle(
        UpdateAssessmentTierCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentTier? tier =
            await _dbContext.AssessmentTiers
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (tier is null)
        {
            return AssessmentErrors.TierNotFound;
        }

        try
        {
            tier.Update(
                request.ScreenOrder,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSubtitle,
                request.EnglishSubtitle,
                request.BackgroundColor,
                request.ArabicButtonText,
                request.EnglishButtonText,
                request.MinScore,
                request.MaxScore,
                request.ArabicRecommendations,
                request.EnglishRecommendations);

            if (!string.IsNullOrWhiteSpace(request.ImagePath))
            {
                tier.ChangeImage(request.ImagePath);
            }
        }
        catch (DomainException)
        {
            return AssessmentErrors.InvalidTier;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return tier.ToAdminResponse();
    }
}

// ------------------------------- Activate -------------------------------

public sealed record ActivateAssessmentTierCommand(
    AssessmentTierId Id)
    : ICommand<AdminAssessmentTierResponse>;

public sealed class ActivateAssessmentTierCommandHandler
    : ICommandHandler<ActivateAssessmentTierCommand, AdminAssessmentTierResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public ActivateAssessmentTierCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentTierResponse>> Handle(
        ActivateAssessmentTierCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentTier? tier =
            await _dbContext.AssessmentTiers
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (tier is null)
        {
            return AssessmentErrors.TierNotFound;
        }

        tier.Activate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return tier.ToAdminResponse();
    }
}

// ------------------------------ Deactivate ------------------------------

public sealed record DeactivateAssessmentTierCommand(
    AssessmentTierId Id)
    : ICommand<AdminAssessmentTierResponse>;

public sealed class DeactivateAssessmentTierCommandHandler
    : ICommandHandler<DeactivateAssessmentTierCommand, AdminAssessmentTierResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public DeactivateAssessmentTierCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentTierResponse>> Handle(
        DeactivateAssessmentTierCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentTier? tier =
            await _dbContext.AssessmentTiers
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (tier is null)
        {
            return AssessmentErrors.TierNotFound;
        }

        tier.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return tier.ToAdminResponse();
    }
}

// --------------------------------- List ---------------------------------

public sealed record ListAdminAssessmentTiersQuery
    : IQuery<IReadOnlyList<AdminAssessmentTierResponse>>;

public sealed class ListAdminAssessmentTiersQueryHandler
    : IQueryHandler<ListAdminAssessmentTiersQuery, IReadOnlyList<AdminAssessmentTierResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListAdminAssessmentTiersQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AdminAssessmentTierResponse>>> Handle(
        ListAdminAssessmentTiersQuery request,
        CancellationToken cancellationToken)
    {
        List<AssessmentTier> tiers =
            await _dbContext.AssessmentTiers
                .AsNoTracking()
                .OrderBy(t => t.ScreenOrder)
                .ToListAsync(cancellationToken);

        IReadOnlyList<AdminAssessmentTierResponse> response =
            tiers.Select(t => t.ToAdminResponse()).ToList();

        return Result<IReadOnlyList<AdminAssessmentTierResponse>>.Success(response);
    }
}

// --------------------------------- Get ----------------------------------

public sealed record GetAdminAssessmentTierQuery(
    AssessmentTierId Id)
    : IQuery<AdminAssessmentTierResponse>;

public sealed class GetAdminAssessmentTierQueryHandler
    : IQueryHandler<GetAdminAssessmentTierQuery, AdminAssessmentTierResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetAdminAssessmentTierQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentTierResponse>> Handle(
        GetAdminAssessmentTierQuery request,
        CancellationToken cancellationToken)
    {
        AssessmentTier? tier =
            await _dbContext.AssessmentTiers
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (tier is null)
        {
            return AssessmentErrors.TierNotFound;
        }

        return tier.ToAdminResponse();
    }
}