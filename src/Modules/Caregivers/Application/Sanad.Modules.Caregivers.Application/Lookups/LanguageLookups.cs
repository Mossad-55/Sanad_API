using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record LanguageResponse(
    LanguageId Id,
    string Code,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record LanguagePublicItem(
    LanguageId Id,
    string Code,
    string ArabicName,
    string EnglishName);

internal static class LanguageMappings
{
    public static LanguageResponse ToResponse(
        this Language language)
    {
        return new LanguageResponse(
            language.Id,
            language.Code,
            language.ArabicName,
            language.EnglishName,
            language.IsActive);
    }

    public static LanguagePublicItem ToPublicItem(
        this Language language)
    {
        return new LanguagePublicItem(
            language.Id,
            language.Code,
            language.ArabicName,
            language.EnglishName);
    }
}

public sealed record CreateLanguageCommand(
    string Code,
    string ArabicName,
    string EnglishName)
    : ICommand<LanguageResponse>;

public sealed class CreateLanguageCommandValidator :
    AbstractValidator<CreateLanguageCommand>
{
    public CreateLanguageCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(3)
            .Matches("^[a-z]{2,3}$");

        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Language.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Language.MaximumNameLength);
    }
}

public sealed class CreateLanguageCommandHandler :
    ICommandHandler<CreateLanguageCommand, LanguageResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateLanguageCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LanguageResponse>> Handle(
        CreateLanguageCommand request,
        CancellationToken cancellationToken)
    {
        string code = request.Code.Trim().ToLowerInvariant();

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicateCode =
            await _dbContext.Languages.AnyAsync(
                language => language.Code == code,
                cancellationToken);

        if (duplicateCode)
        {
            return LookupsErrors.LanguageCodeInUse;
        }

        bool duplicateName =
            await _dbContext.Languages.AnyAsync(
                language =>
                    language.ArabicName == arabicName ||
                    language.EnglishName == englishName,
                cancellationToken);

        if (duplicateName)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Language language =
            Language.Create(
                request.Code,
                request.ArabicName,
                request.EnglishName);

        _dbContext.Languages.Add(language);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return language.ToResponse();
    }
}

public sealed record RenameLanguageCommand(
    LanguageId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<LanguageResponse>;

public sealed class RenameLanguageCommandValidator :
    AbstractValidator<RenameLanguageCommand>
{
    public RenameLanguageCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(LanguageId.Empty);

        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Language.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Language.MaximumNameLength);
    }
}

public sealed class RenameLanguageCommandHandler :
    ICommandHandler<RenameLanguageCommand, LanguageResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameLanguageCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LanguageResponse>> Handle(
        RenameLanguageCommand request,
        CancellationToken cancellationToken)
    {
        Language? language =
            await _dbContext.Languages.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (language is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Languages.AnyAsync(
                item =>
                    item.Id != request.Id &&
                    (item.ArabicName == arabicName ||
                     item.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        language.UpdateNames(
            request.ArabicName,
            request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return language.ToResponse();
    }
}

public sealed record SetLanguageActiveCommand(
    LanguageId Id,
    bool IsActive)
    : ICommand<LanguageResponse>;

public sealed class SetLanguageActiveCommandValidator :
    AbstractValidator<SetLanguageActiveCommand>
{
    public SetLanguageActiveCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(LanguageId.Empty);
    }
}

public sealed class SetLanguageActiveCommandHandler :
    ICommandHandler<SetLanguageActiveCommand, LanguageResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetLanguageActiveCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LanguageResponse>> Handle(
        SetLanguageActiveCommand request,
        CancellationToken cancellationToken)
    {
        Language? language =
            await _dbContext.Languages.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (language is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            language.Activate();
        }
        else
        {
            language.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return language.ToResponse();
    }
}

public sealed record GetActiveLanguagesQuery()
    : IQuery<IReadOnlyList<LanguagePublicItem>>;

public sealed class GetActiveLanguagesQueryHandler :
    IQueryHandler<
        GetActiveLanguagesQuery,
        IReadOnlyList<LanguagePublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveLanguagesQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<LanguagePublicItem>>> Handle(
        GetActiveLanguagesQuery request,
        CancellationToken cancellationToken)
    {
        List<Language> languages =
            await _dbContext.Languages
                .AsNoTracking()
                .Where(language => language.IsActive)
                .OrderBy(language => language.Code)
                .ToListAsync(cancellationToken);

        IReadOnlyList<LanguagePublicItem> items =
            languages
                .Select(language => language.ToPublicItem())
                .ToList();

        return Result<IReadOnlyList<LanguagePublicItem>>.Success(items);
    }

    public sealed record GetAllLanguagesQuery()
        : IQuery<IReadOnlyList<LanguageResponse>>;

    public sealed class GetAllLanguagesQueryHandler :
        IQueryHandler<GetAllLanguagesQuery, IReadOnlyList<LanguageResponse>>
    {
        private readonly ICaregiversDbContext _dbContext;

        public GetAllLanguagesQueryHandler(ICaregiversDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result<IReadOnlyList<LanguageResponse>>> Handle(
            GetAllLanguagesQuery request,
            CancellationToken cancellationToken)
        {
            List<Language> languages =
                await _dbContext.Languages
                    .AsNoTracking()
                    .OrderBy(language => language.Code)
                    .ToListAsync(cancellationToken);

            IReadOnlyList<LanguageResponse> items =
                languages
                    .Select(language => language.ToResponse())
                    .ToList();

            return Result<IReadOnlyList<LanguageResponse>>.Success(items);
        }
    }
}