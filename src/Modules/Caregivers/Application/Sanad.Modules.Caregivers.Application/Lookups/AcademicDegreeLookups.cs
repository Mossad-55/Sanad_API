using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record AcademicDegreeResponse(
    AcademicDegreeId Id,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record AcademicDegreePublicItem(
    AcademicDegreeId Id,
    string ArabicName,
    string EnglishName);

internal static class AcademicDegreeMappings
{
    public static AcademicDegreeResponse ToResponse(this AcademicDegree d) =>
        new(d.Id, d.ArabicName, d.EnglishName, d.IsActive);

    public static AcademicDegreePublicItem ToPublicItem(this AcademicDegree d) =>
        new(d.Id, d.ArabicName, d.EnglishName);
}

public sealed record CreateAcademicDegreeCommand(
    string ArabicName,
    string EnglishName,
    bool IsActive)
    : ICommand<AcademicDegreeResponse>;

public sealed class CreateAcademicDegreeCommandValidator : AbstractValidator<CreateAcademicDegreeCommand>
{
    public CreateAcademicDegreeCommandValidator()
    {
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(AcademicDegree.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(AcademicDegree.MaximumNameLength);
    }
}

public sealed class CreateAcademicDegreeCommandHandler :
    ICommandHandler<CreateAcademicDegreeCommand, AcademicDegreeResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateAcademicDegreeCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AcademicDegreeResponse>> Handle(
        CreateAcademicDegreeCommand request,
        CancellationToken cancellationToken)
    {
        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.AcademicDegrees.AnyAsync(
                d => d.ArabicName == arabicName || d.EnglishName == englishName,
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        AcademicDegree degree =
            AcademicDegree.Create(
                request.ArabicName,
                request.EnglishName,
                request.IsActive);

        _dbContext.AcademicDegrees.Add(degree);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return degree.ToResponse();
    }
}

public sealed record RenameAcademicDegreeCommand(
    AcademicDegreeId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<AcademicDegreeResponse>;

public sealed class RenameAcademicDegreeCommandValidator : AbstractValidator<RenameAcademicDegreeCommand>
{
    public RenameAcademicDegreeCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AcademicDegreeId.Empty);
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(AcademicDegree.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(AcademicDegree.MaximumNameLength);
    }
}

public sealed class RenameAcademicDegreeCommandHandler :
    ICommandHandler<RenameAcademicDegreeCommand, AcademicDegreeResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameAcademicDegreeCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AcademicDegreeResponse>> Handle(
        RenameAcademicDegreeCommand request,
        CancellationToken cancellationToken)
    {
        AcademicDegree? degree =
            await _dbContext.AcademicDegrees.SingleOrDefaultAsync(
                d => d.Id == request.Id,
                cancellationToken);

        if (degree is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.AcademicDegrees.AnyAsync(
                other =>
                    other.Id != request.Id &&
                    (other.ArabicName == arabicName || other.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        degree.UpdateNames(request.ArabicName, request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return degree.ToResponse();
    }
}

public sealed record SetAcademicDegreeActiveCommand(
    AcademicDegreeId Id,
    bool IsActive)
    : ICommand<AcademicDegreeResponse>;

public sealed class SetAcademicDegreeActiveCommandValidator : AbstractValidator<SetAcademicDegreeActiveCommand>
{
    public SetAcademicDegreeActiveCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AcademicDegreeId.Empty);
    }
}

public sealed class SetAcademicDegreeActiveCommandHandler :
    ICommandHandler<SetAcademicDegreeActiveCommand, AcademicDegreeResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetAcademicDegreeActiveCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AcademicDegreeResponse>> Handle(
        SetAcademicDegreeActiveCommand request,
        CancellationToken cancellationToken)
    {
        AcademicDegree? degree =
            await _dbContext.AcademicDegrees.SingleOrDefaultAsync(
                d => d.Id == request.Id,
                cancellationToken);

        if (degree is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            degree.Activate();
        }
        else
        {
            degree.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return degree.ToResponse();
    }
}

public sealed record GetAllAcademicDegreesQuery()
    : IQuery<IReadOnlyList<AcademicDegreeResponse>>;

public sealed class GetAllAcademicDegreesQueryHandler :
    IQueryHandler<GetAllAcademicDegreesQuery, IReadOnlyList<AcademicDegreeResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllAcademicDegreesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AcademicDegreeResponse>>> Handle(
        GetAllAcademicDegreesQuery request,
        CancellationToken cancellationToken)
    {
        List<AcademicDegree> items =
            await _dbContext.AcademicDegrees
                .AsNoTracking()
                .OrderBy(d => d.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AcademicDegreeResponse>>.Success(
            items.Select(d => d.ToResponse()).ToList());
    }
}

public sealed record GetActiveAcademicDegreesQuery()
    : IQuery<IReadOnlyList<AcademicDegreePublicItem>>;

public sealed class GetActiveAcademicDegreesQueryHandler :
    IQueryHandler<GetActiveAcademicDegreesQuery, IReadOnlyList<AcademicDegreePublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveAcademicDegreesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AcademicDegreePublicItem>>> Handle(
        GetActiveAcademicDegreesQuery request,
        CancellationToken cancellationToken)
    {
        List<AcademicDegree> items =
            await _dbContext.AcademicDegrees
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AcademicDegreePublicItem>>.Success(
            items.Select(d => d.ToPublicItem()).ToList());
    }
}