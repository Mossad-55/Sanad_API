using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record SpecializationResponse(
    SpecializationId Id,
    string ArabicName,
    string EnglishName,
    CaregiverType CaregiverType,
    bool IsActive);

public sealed record SpecializationPublicItem(
    SpecializationId Id,
    string ArabicName,
    string EnglishName,
    CaregiverType CaregiverType);

internal static class SpecializationMappings
{
    public static SpecializationResponse ToResponse(this Specialization s) =>
        new(s.Id, s.ArabicName, s.EnglishName, s.CaregiverType, s.IsActive);

    public static SpecializationPublicItem ToPublicItem(this Specialization s) =>
        new(s.Id, s.ArabicName, s.EnglishName, s.CaregiverType);
}

public sealed record CreateSpecializationCommand(
    string ArabicName,
    string EnglishName,
    CaregiverType CaregiverType,
    bool IsActive)
    : ICommand<SpecializationResponse>;

public sealed class CreateSpecializationCommandValidator : AbstractValidator<CreateSpecializationCommand>
{
    public CreateSpecializationCommandValidator()
    {
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(Specialization.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(Specialization.MaximumNameLength);
        RuleFor(c => c.CaregiverType).IsInEnum();
    }
}

public sealed class CreateSpecializationCommandHandler :
    ICommandHandler<CreateSpecializationCommand, SpecializationResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateSpecializationCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SpecializationResponse>> Handle(
        CreateSpecializationCommand request,
        CancellationToken cancellationToken)
    {
        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Specializations.AnyAsync(
                s => s.CaregiverType == request.CaregiverType &&
                     (s.ArabicName == arabicName || s.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Specialization specialization =
            Specialization.Create(
                request.ArabicName,
                request.EnglishName,
                request.IsActive,
                request.CaregiverType);

        _dbContext.Specializations.Add(specialization);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return specialization.ToResponse();
    }
}

public sealed record RenameSpecializationCommand(
    SpecializationId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<SpecializationResponse>;

public sealed class RenameSpecializationCommandValidator : AbstractValidator<RenameSpecializationCommand>
{
    public RenameSpecializationCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(SpecializationId.Empty);
        RuleFor(c => c.ArabicName).NotEmpty().MaximumLength(Specialization.MaximumNameLength);
        RuleFor(c => c.EnglishName).NotEmpty().MaximumLength(Specialization.MaximumNameLength);
    }
}

public sealed class RenameSpecializationCommandHandler :
    ICommandHandler<RenameSpecializationCommand, SpecializationResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameSpecializationCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SpecializationResponse>> Handle(
        RenameSpecializationCommand request,
        CancellationToken cancellationToken)
    {
        Specialization? specialization =
            await _dbContext.Specializations.SingleOrDefaultAsync(
                s => s.Id == request.Id,
                cancellationToken);

        if (specialization is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Specializations.AnyAsync(
                other =>
                    other.Id != request.Id &&
                    other.CaregiverType == specialization.CaregiverType &&
                    (other.ArabicName == arabicName || other.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        specialization.UpdateNames(request.ArabicName, request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return specialization.ToResponse();
    }
}

public sealed record SetSpecializationActiveCommand(
    SpecializationId Id,
    bool IsActive)
    : ICommand<SpecializationResponse>;

public sealed class SetSpecializationActiveCommandValidator : AbstractValidator<SetSpecializationActiveCommand>
{
    public SetSpecializationActiveCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(SpecializationId.Empty);
    }
}

public sealed class SetSpecializationActiveCommandHandler :
    ICommandHandler<SetSpecializationActiveCommand, SpecializationResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetSpecializationActiveCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SpecializationResponse>> Handle(
        SetSpecializationActiveCommand request,
        CancellationToken cancellationToken)
    {
        Specialization? specialization =
            await _dbContext.Specializations.SingleOrDefaultAsync(
                s => s.Id == request.Id,
                cancellationToken);

        if (specialization is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            specialization.Activate();
        }
        else
        {
            specialization.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return specialization.ToResponse();
    }
}

public sealed record GetAllSpecializationsQuery()
    : IQuery<IReadOnlyList<SpecializationResponse>>;

public sealed class GetAllSpecializationsQueryHandler :
    IQueryHandler<GetAllSpecializationsQuery, IReadOnlyList<SpecializationResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllSpecializationsQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SpecializationResponse>>> Handle(
        GetAllSpecializationsQuery request,
        CancellationToken cancellationToken)
    {
        List<Specialization> items =
            await _dbContext.Specializations
                .AsNoTracking()
                .OrderBy(s => s.CaregiverType)
                .ThenBy(s => s.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SpecializationResponse>>.Success(
            items.Select(s => s.ToResponse()).ToList());
    }
}

public sealed record GetActiveSpecializationsQuery()
    : IQuery<IReadOnlyList<SpecializationPublicItem>>;

public sealed class GetActiveSpecializationsQueryHandler :
    IQueryHandler<GetActiveSpecializationsQuery, IReadOnlyList<SpecializationPublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveSpecializationsQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SpecializationPublicItem>>> Handle(
        GetActiveSpecializationsQuery request,
        CancellationToken cancellationToken)
    {
        List<Specialization> items =
            await _dbContext.Specializations
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.CaregiverType)
                .ThenBy(s => s.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SpecializationPublicItem>>.Success(
            items.Select(s => s.ToPublicItem()).ToList());
    }
}