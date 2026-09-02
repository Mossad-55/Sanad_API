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

public sealed record AdminOptionInput(
    int Order,
    string ArabicText,
    string EnglishText,
    int Weight);

public sealed record AdminAssessmentOptionResponse(
    AssessmentOptionId Id,
    int Order,
    string ArabicText,
    string EnglishText,
    int Weight);

public sealed record AdminAssessmentQuestionResponse(
    AssessmentQuestionId Id,
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    bool IsActive,
    IReadOnlyList<AdminAssessmentOptionResponse> Options,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

internal static class AssessmentQuestionMappings
{
    public static AdminAssessmentQuestionResponse ToAdminResponse(
        this AssessmentQuestion question) =>
        new(
            question.Id,
            question.Order,
            question.ArabicText,
            question.EnglishText,
            question.IsRequired,
            question.IsActive,
            question.Options
                .OrderBy(o => o.Order)
                .Select(o => new AdminAssessmentOptionResponse(
                    o.Id,
                    o.Order,
                    o.ArabicText,
                    o.EnglishText,
                    o.Weight))
                .ToList(),
            question.CreatedOnUtc,
            question.UpdatedOnUtc);
}

// -------------------------------- Create --------------------------------

public sealed record CreateAssessmentQuestionCommand(
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    bool IsActive,
    IReadOnlyList<AdminOptionInput> Options)
    : ICommand<AdminAssessmentQuestionResponse>;

public sealed class CreateAssessmentQuestionCommandValidator
    : AbstractValidator<CreateAssessmentQuestionCommand>
{
    public CreateAssessmentQuestionCommandValidator()
    {
        RuleFor(c => c.Order).GreaterThan(0);
        RuleFor(c => c.ArabicText).NotEmpty().MaximumLength(AssessmentQuestion.MaximumTextLength);
        RuleFor(c => c.EnglishText).NotEmpty().MaximumLength(AssessmentQuestion.MaximumTextLength);
        RuleFor(c => c.Options)
            .NotNull()
            .Must(o => o.Count >= AssessmentQuestion.MinimumOptionsCount)
            .WithMessage($"Question must have at least {AssessmentQuestion.MinimumOptionsCount} options.")
            .Must(o => o.Count <= AssessmentQuestion.MaximumOptionsCount)
            .WithMessage($"Question cannot have more than {AssessmentQuestion.MaximumOptionsCount} options.");

        RuleForEach(c => c.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Order).GreaterThan(0);
            option.RuleFor(o => o.ArabicText).NotEmpty().MaximumLength(AssessmentOption.MaximumTextLength);
            option.RuleFor(o => o.EnglishText).NotEmpty().MaximumLength(AssessmentOption.MaximumTextLength);
            option.RuleFor(o => o.Weight).InclusiveBetween(0, AssessmentOption.MaximumWeight);
        });
    }
}

public sealed class CreateAssessmentQuestionCommandHandler
    : ICommandHandler<CreateAssessmentQuestionCommand, AdminAssessmentQuestionResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public CreateAssessmentQuestionCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentQuestionResponse>> Handle(
        CreateAssessmentQuestionCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentQuestion question;
        try
        {
            question = AssessmentQuestion.Create(
                request.Order,
                request.ArabicText,
                request.EnglishText,
                request.IsRequired,
                request.IsActive);

            question.SetOptions(
                request.Options.Select(o => (o.Order, o.ArabicText, o.EnglishText, o.Weight)));
        }
        catch (DomainException)
        {
            return AssessmentErrors.InvalidQuestion;
        }

        _dbContext.AssessmentQuestions.Add(question);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return question.ToAdminResponse();
    }
}

// -------------------------------- Update --------------------------------

public sealed record UpdateAssessmentQuestionCommand(
    AssessmentQuestionId Id,
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    IReadOnlyList<AdminOptionInput> Options)
    : ICommand<AdminAssessmentQuestionResponse>;

public sealed class UpdateAssessmentQuestionCommandValidator
    : AbstractValidator<UpdateAssessmentQuestionCommand>
{
    public UpdateAssessmentQuestionCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AssessmentQuestionId.Empty);
        RuleFor(c => c.Order).GreaterThan(0);
        RuleFor(c => c.ArabicText).NotEmpty().MaximumLength(AssessmentQuestion.MaximumTextLength);
        RuleFor(c => c.EnglishText).NotEmpty().MaximumLength(AssessmentQuestion.MaximumTextLength);
        RuleFor(c => c.Options)
            .NotNull()
            .Must(o => o.Count >= AssessmentQuestion.MinimumOptionsCount)
            .WithMessage($"Question must have at least {AssessmentQuestion.MinimumOptionsCount} options.")
            .Must(o => o.Count <= AssessmentQuestion.MaximumOptionsCount)
            .WithMessage($"Question cannot have more than {AssessmentQuestion.MaximumOptionsCount} options.");

        RuleForEach(c => c.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Order).GreaterThan(0);
            option.RuleFor(o => o.ArabicText).NotEmpty().MaximumLength(AssessmentOption.MaximumTextLength);
            option.RuleFor(o => o.EnglishText).NotEmpty().MaximumLength(AssessmentOption.MaximumTextLength);
            option.RuleFor(o => o.Weight).InclusiveBetween(0, AssessmentOption.MaximumWeight);
        });
    }
}

public sealed class UpdateAssessmentQuestionCommandHandler
    : ICommandHandler<UpdateAssessmentQuestionCommand, AdminAssessmentQuestionResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateAssessmentQuestionCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentQuestionResponse>> Handle(
        UpdateAssessmentQuestionCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentQuestion? question =
            await _dbContext.AssessmentQuestions
                .Include(q => q.Options)
                .SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken);

        if (question is null)
        {
            return AssessmentErrors.QuestionNotFound;
        }

        try
        {
            question.UpdateDetails(
                request.Order,
                request.ArabicText,
                request.EnglishText,
                request.IsRequired);

            question.SetOptions(
                request.Options.Select(o => (o.Order, o.ArabicText, o.EnglishText, o.Weight)));
        }
        catch (DomainException)
        {
            return AssessmentErrors.InvalidQuestion;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return question.ToAdminResponse();
    }
}

// ------------------------------- Activate -------------------------------

public sealed record ActivateAssessmentQuestionCommand(
    AssessmentQuestionId Id)
    : ICommand<AdminAssessmentQuestionResponse>;

public sealed class ActivateAssessmentQuestionCommandHandler
    : ICommandHandler<ActivateAssessmentQuestionCommand, AdminAssessmentQuestionResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public ActivateAssessmentQuestionCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentQuestionResponse>> Handle(
        ActivateAssessmentQuestionCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentQuestion? question =
            await _dbContext.AssessmentQuestions
                .Include(q => q.Options)
                .SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken);

        if (question is null)
        {
            return AssessmentErrors.QuestionNotFound;
        }

        question.Activate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return question.ToAdminResponse();
    }
}

// ------------------------------ Deactivate ------------------------------

public sealed record DeactivateAssessmentQuestionCommand(
    AssessmentQuestionId Id)
    : ICommand<AdminAssessmentQuestionResponse>;

public sealed class DeactivateAssessmentQuestionCommandHandler
    : ICommandHandler<DeactivateAssessmentQuestionCommand, AdminAssessmentQuestionResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public DeactivateAssessmentQuestionCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentQuestionResponse>> Handle(
        DeactivateAssessmentQuestionCommand request,
        CancellationToken cancellationToken)
    {
        AssessmentQuestion? question =
            await _dbContext.AssessmentQuestions
                .Include(q => q.Options)
                .SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken);

        if (question is null)
        {
            return AssessmentErrors.QuestionNotFound;
        }

        question.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return question.ToAdminResponse();
    }
}

// --------------------------------- List ---------------------------------

public sealed record ListAdminAssessmentQuestionsQuery
    : IQuery<IReadOnlyList<AdminAssessmentQuestionResponse>>;

public sealed class ListAdminAssessmentQuestionsQueryHandler
    : IQueryHandler<ListAdminAssessmentQuestionsQuery, IReadOnlyList<AdminAssessmentQuestionResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListAdminAssessmentQuestionsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AdminAssessmentQuestionResponse>>> Handle(
        ListAdminAssessmentQuestionsQuery request,
        CancellationToken cancellationToken)
    {
        List<AssessmentQuestion> questions =
            await _dbContext.AssessmentQuestions
                .AsNoTracking()
                .Include(q => q.Options)
                .OrderBy(q => q.Order)
                .ToListAsync(cancellationToken);

        IReadOnlyList<AdminAssessmentQuestionResponse> response =
            questions.Select(q => q.ToAdminResponse()).ToList();

        return Result<IReadOnlyList<AdminAssessmentQuestionResponse>>.Success(response);
    }
}

// --------------------------------- Get ----------------------------------

public sealed record GetAdminAssessmentQuestionQuery(
    AssessmentQuestionId Id)
    : IQuery<AdminAssessmentQuestionResponse>;

public sealed class GetAdminAssessmentQuestionQueryHandler
    : IQueryHandler<GetAdminAssessmentQuestionQuery, AdminAssessmentQuestionResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetAdminAssessmentQuestionQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminAssessmentQuestionResponse>> Handle(
        GetAdminAssessmentQuestionQuery request,
        CancellationToken cancellationToken)
    {
        AssessmentQuestion? question =
            await _dbContext.AssessmentQuestions
                .AsNoTracking()
                .Include(q => q.Options)
                .SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken);

        if (question is null)
        {
            return AssessmentErrors.QuestionNotFound;
        }

        return question.ToAdminResponse();
    }
}