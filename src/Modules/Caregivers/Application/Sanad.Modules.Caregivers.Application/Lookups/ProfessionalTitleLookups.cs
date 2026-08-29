using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record ProfessionalTitleResponse(
    ProfessionalTitleId Id,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record ProfessionalTitlePublicItem(
    ProfessionalTitleId Id,
    string ArabicName,
    string EnglishName);

internal static class ProfessionalTitleMappings
{
    public static ProfessionalTitleResponse ToResponse(this ProfessionalTitle t) =>
        new(t.Id, t.ArabicName, t.EnglishName, t.IsActive);

    public static ProfessionalTitlePublicItem ToPublicItem(this ProfessionalTitle t) =>
        new(t.Id, t.ArabicName, t.EnglishName);
}

public sealed record CreateProfessionalTitleCommand(
    string ArabicName,
    string EnglishName,
    bool IsActive)
    : ICommand<ProfessionalTitleResponse>;

public sealed class CreateProfessionalTitleCommandValidator : AbstractValidator<CreateProfessionalTitleCommand>
{
    public CreateProfessionalTitleCommandValidator()
    {
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(ProfessionalTitle.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(ProfessionalTitle.MaximumNameLength);
    }
}

public sealed class CreateProfessionalTitleCommandHandler :
    ICommandHandler<CreateProfessionalTitleCommand, ProfessionalTitleResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateProfessionalTitleCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ProfessionalTitleResponse>> Handle(
        CreateProfessionalTitleCommand request,
        CancellationToken cancellationToken)
    {
        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.ProfessionalTitles.AnyAsync(
                t => t.ArabicName == arabicName || t.EnglishName == englishName,
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        ProfessionalTitle title =
            ProfessionalTitle.Create(
                request.ArabicName,
                request.EnglishName,
                request.IsActive);

        _dbContext.ProfessionalTitles.Add(title);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return title.ToResponse();
    }
}

public sealed record RenameProfessionalTitleCommand(
    ProfessionalTitleId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<ProfessionalTitleResponse>;

public sealed class RenameProfessionalTitleCommandValidator : AbstractValidator<RenameProfessionalTitleCommand>
{
    public RenameProfessionalTitleCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(ProfessionalTitleId.Empty);
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(ProfessionalTitle.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(ProfessionalTitle.MaximumNameLength);
    }
}

public sealed class RenameProfessionalTitleCommandHandler :
    ICommandHandler<RenameProfessionalTitleCommand, ProfessionalTitleResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameProfessionalTitleCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ProfessionalTitleResponse>> Handle(
        RenameProfessionalTitleCommand request,
        CancellationToken cancellationToken)
    {
        ProfessionalTitle? title =
            await _dbContext.ProfessionalTitles.SingleOrDefaultAsync(
                t => t.Id == request.Id,
                cancellationToken);

        if (title is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.ProfessionalTitles.AnyAsync(
                other =>
                    other.Id != request.Id &&
                    (other.ArabicName == arabicName || other.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        title.UpdateNames(request.ArabicName, request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return title.ToResponse();
    }
}

public sealed record SetProfessionalTitleActiveCommand(
    ProfessionalTitleId Id,
    bool IsActive)
    : ICommand<ProfessionalTitleResponse>;

public sealed class SetProfessionalTitleActiveCommandValidator : AbstractValidator<SetProfessionalTitleActiveCommand>
{
    public SetProfessionalTitleActiveCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(ProfessionalTitleId.Empty);
    }
}

public sealed class SetProfessionalTitleActiveCommandHandler :
    ICommandHandler<SetProfessionalTitleActiveCommand, ProfessionalTitleResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetProfessionalTitleActiveCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ProfessionalTitleResponse>> Handle(
        SetProfessionalTitleActiveCommand request,
        CancellationToken cancellationToken)
    {
        ProfessionalTitle? title =
            await _dbContext.ProfessionalTitles.SingleOrDefaultAsync(
                t => t.Id == request.Id,
                cancellationToken);

        if (title is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            title.Activate();
        }
        else
        {
            title.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return title.ToResponse();
    }
}

public sealed record GetAllProfessionalTitlesQuery()
    : IQuery<IReadOnlyList<ProfessionalTitleResponse>>;

public sealed class GetAllProfessionalTitlesQueryHandler :
    IQueryHandler<GetAllProfessionalTitlesQuery, IReadOnlyList<ProfessionalTitleResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllProfessionalTitlesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ProfessionalTitleResponse>>> Handle(
        GetAllProfessionalTitlesQuery request,
        CancellationToken cancellationToken)
    {
        List<ProfessionalTitle> items =
            await _dbContext.ProfessionalTitles
                .AsNoTracking()
                .OrderBy(t => t.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ProfessionalTitleResponse>>.Success(
            items.Select(t => t.ToResponse()).ToList());
    }
}

public sealed record GetActiveProfessionalTitlesQuery()
    : IQuery<IReadOnlyList<ProfessionalTitlePublicItem>>;

public sealed class GetActiveProfessionalTitlesQueryHandler :
    IQueryHandler<GetActiveProfessionalTitlesQuery, IReadOnlyList<ProfessionalTitlePublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveProfessionalTitlesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ProfessionalTitlePublicItem>>> Handle(
        GetActiveProfessionalTitlesQuery request,
        CancellationToken cancellationToken)
    {
        List<ProfessionalTitle> items =
            await _dbContext.ProfessionalTitles
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ProfessionalTitlePublicItem>>.Success(
            items.Select(t => t.ToPublicItem()).ToList());
    }
}