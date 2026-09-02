using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.Modules.Families.Application.Assessments;

// --------------------------------- DTOs ---------------------------------

public sealed record FamilyAssessmentOptionResponse(
    AssessmentOptionId Id,
    int Order,
    string ArabicText,
    string EnglishText);

public sealed record FamilyAssessmentQuestionResponse(
    AssessmentQuestionId Id,
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    IReadOnlyList<FamilyAssessmentOptionResponse> Options);

public sealed record FamilyAssessmentTierResponse(
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
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations);

public sealed record FamilyAssessmentResultResponse(
    CareAssessmentId AssessmentId,
    int TotalScore,
    FamilyAssessmentTierResponse Tier,
    DateTime CompletedOnUtc);

internal static class FamilyAssessmentMappings
{
    public static FamilyAssessmentQuestionResponse ToFamilyResponse(
        this AssessmentQuestion question) =>
        new(
            question.Id,
            question.Order,
            question.ArabicText,
            question.EnglishText,
            question.IsRequired,
            question.Options
                .OrderBy(o => o.Order)
                // Critical: option weights are omitted for mobile clients.
                .Select(o => new FamilyAssessmentOptionResponse(
                    o.Id,
                    o.Order,
                    o.ArabicText,
                    o.EnglishText))
                .ToList());

    public static FamilyAssessmentTierResponse ToFamilyResponse(
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
            tier.ArabicRecommendations,
            tier.EnglishRecommendations);
}

// ---------------------------- List Questions ----------------------------

public sealed record GetFamilyAssessmentQuestionsQuery
    : IQuery<IReadOnlyList<FamilyAssessmentQuestionResponse>>;

public sealed class GetFamilyAssessmentQuestionsQueryHandler
    : IQueryHandler<GetFamilyAssessmentQuestionsQuery, IReadOnlyList<FamilyAssessmentQuestionResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyAssessmentQuestionsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<FamilyAssessmentQuestionResponse>>> Handle(
        GetFamilyAssessmentQuestionsQuery request,
        CancellationToken cancellationToken)
    {
        List<AssessmentQuestion> activeQuestions =
            await _dbContext.AssessmentQuestions
                .AsNoTracking()
                .Include(q => q.Options)
                .Where(q => q.IsActive)
                .OrderBy(q => q.Order)
                .ToListAsync(cancellationToken);

        IReadOnlyList<FamilyAssessmentQuestionResponse> response =
            activeQuestions.Select(q => q.ToFamilyResponse()).ToList();

        return Result<IReadOnlyList<FamilyAssessmentQuestionResponse>>.Success(response);
    }
}

// ------------------------------ List Tiers ------------------------------

public sealed record GetFamilyAssessmentTiersQuery
    : IQuery<IReadOnlyList<FamilyAssessmentTierResponse>>;

public sealed class GetFamilyAssessmentTiersQueryHandler
    : IQueryHandler<GetFamilyAssessmentTiersQuery, IReadOnlyList<FamilyAssessmentTierResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyAssessmentTiersQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<FamilyAssessmentTierResponse>>> Handle(
        GetFamilyAssessmentTiersQuery request,
        CancellationToken cancellationToken)
    {
        List<AssessmentTier> activeTiers =
            await _dbContext.AssessmentTiers
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.ScreenOrder)
                .ToListAsync(cancellationToken);

        IReadOnlyList<FamilyAssessmentTierResponse> response =
            activeTiers.Select(t => t.ToFamilyResponse()).ToList();

        return Result<IReadOnlyList<FamilyAssessmentTierResponse>>.Success(response);
    }
}

// --------------------------- Submit Assessment --------------------------

public sealed record AssessmentAnswerInput(
    AssessmentQuestionId QuestionId,
    AssessmentOptionId SelectedOptionId);

public sealed record SubmitAssessmentCommand(
    UserId UserId,
    ElderlyId? ElderlyId,
    IReadOnlyList<AssessmentAnswerInput> Answers,
    DateTime UtcNow)
    : ICommand<FamilyAssessmentResultResponse>;

public sealed class SubmitAssessmentCommandValidator
    : AbstractValidator<SubmitAssessmentCommand>
{
    public SubmitAssessmentCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Answers)
            .NotNull()
            .Must(a => a.Count > 0)
            .WithMessage("At least one answer must be submitted.");

        RuleForEach(c => c.Answers).ChildRules(answer =>
        {
            answer.RuleFor(a => a.QuestionId).NotEqual(AssessmentQuestionId.Empty);
            answer.RuleFor(a => a.SelectedOptionId).NotEqual(AssessmentOptionId.Empty);
        });
    }
}

public sealed class SubmitAssessmentCommandHandler
    : ICommandHandler<SubmitAssessmentCommand, FamilyAssessmentResultResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public SubmitAssessmentCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<FamilyAssessmentResultResponse>> Handle(
        SubmitAssessmentCommand request,
        CancellationToken cancellationToken)
    {
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return ElderlyErrors.FamilyNotFound;
        }

        List<AssessmentQuestion> activeQuestions =
            await _dbContext.AssessmentQuestions
                .Include(q => q.Options)
                .Where(q => q.IsActive)
                .ToListAsync(cancellationToken);

        if (activeQuestions.Count == 0)
        {
            return AssessmentErrors.QuestionNotFound;
        }

        // 1. Verify required questions are all answered.
        var submittedQuestionIds = request.Answers
            .Select(a => a.QuestionId)
            .ToHashSet();

        bool hasMissingRequired = activeQuestions
            .Where(q => q.IsRequired)
            .Any(q => !submittedQuestionIds.Contains(q.Id));

        if (hasMissingRequired)
        {
            return AssessmentErrors.InvalidSubmission;
        }

        // 2. Score calculation & answer verification.
        int totalScore = 0;
        var answerSnapshots = new List<(AssessmentQuestionId, AssessmentOptionId, int)>();

        foreach (var answer in request.Answers)
        {
            var question = activeQuestions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question is null)
            {
                return AssessmentErrors.InvalidSubmission;
            }

            var option = question.Options.FirstOrDefault(o => o.Id == answer.SelectedOptionId);
            if (option is null)
            {
                return AssessmentErrors.InvalidSubmission;
            }

            totalScore += option.Weight;
            answerSnapshots.Add((question.Id, option.Id, option.Weight));
        }

        // 3. Resolve matching Care Tier.
        List<AssessmentTier> activeTiers =
            await _dbContext.AssessmentTiers
                .Where(t => t.IsActive)
                .OrderBy(t => t.ScreenOrder)
                .ToListAsync(cancellationToken);

        if (activeTiers.Count == 0)
        {
            return AssessmentErrors.TierNotFound;
        }

        AssessmentTier matchedTier =
            activeTiers.FirstOrDefault(t => t.MatchesScore(totalScore))
            ?? activeTiers.OrderByDescending(t => t.MaxScore).First();

        // 4. Create and persist CareAssessment.
        CareAssessment assessment;
        try
        {
            assessment = CareAssessment.Create(
                family.Id,
                request.ElderlyId,
                matchedTier.Id,
                totalScore,
                answerSnapshots,
                request.UtcNow);
        }
        catch (DomainException)
        {
            return AssessmentErrors.InvalidSubmission;
        }

        _dbContext.CareAssessments.Add(assessment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FamilyAssessmentResultResponse(
            assessment.Id,
            assessment.TotalScore,
            matchedTier.ToFamilyResponse(),
            assessment.CompletedOnUtc);
    }
}