using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record GovernorateResponse(
    GovernorateId Id,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record GovernoratePublicItem(
    GovernorateId Id,
    string ArabicName,
    string EnglishName);

internal static class GovernorateMappings
{
    public static GovernorateResponse ToResponse(
        this Governorate governorate)
    {
        return new GovernorateResponse(
            governorate.Id,
            governorate.ArabicName,
            governorate.EnglishName,
            governorate.IsActive);
    }

    public static GovernoratePublicItem ToPublicItem(
        this Governorate governorate)
    {
        return new GovernoratePublicItem(
            governorate.Id,
            governorate.ArabicName,
            governorate.EnglishName);
    }
}

public sealed record CreateGovernorateCommand(
    string ArabicName,
    string EnglishName)
    : ICommand<GovernorateResponse>;

public sealed class CreateGovernorateCommandValidator :
    AbstractValidator<CreateGovernorateCommand>
{
    public CreateGovernorateCommandValidator()
    {
        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Governorate.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Governorate.MaximumNameLength);
    }
}

public sealed class CreateGovernorateCommandHandler :
    ICommandHandler<CreateGovernorateCommand, GovernorateResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateGovernorateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GovernorateResponse>> Handle(
        CreateGovernorateCommand request,
        CancellationToken cancellationToken)
    {
        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Governorates.AnyAsync(
                governorate =>
                    governorate.ArabicName == arabicName ||
                    governorate.EnglishName == englishName,
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Governorate governorate =
            Governorate.Create(
                request.ArabicName,
                request.EnglishName);

        _dbContext.Governorates.Add(governorate);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return governorate.ToResponse();
    }
}

public sealed record RenameGovernorateCommand(
    GovernorateId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<GovernorateResponse>;

public sealed class RenameGovernorateCommandValidator :
    AbstractValidator<RenameGovernorateCommand>
{
    public RenameGovernorateCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(GovernorateId.Empty);

        RuleFor(command => command.ArabicName)
            .NotEmpty()
            .MaximumLength(Governorate.MaximumNameLength);

        RuleFor(command => command.EnglishName)
            .NotEmpty()
            .MaximumLength(Governorate.MaximumNameLength);
    }
}

public sealed class RenameGovernorateCommandHandler :
    ICommandHandler<RenameGovernorateCommand, GovernorateResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameGovernorateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GovernorateResponse>> Handle(
        RenameGovernorateCommand request,
        CancellationToken cancellationToken)
    {
        Governorate? governorate =
            await _dbContext.Governorates.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (governorate is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Governorates.AnyAsync(
                item =>
                    item.Id != request.Id &&
                    (item.ArabicName == arabicName ||
                     item.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        governorate.UpdateNames(
            request.ArabicName,
            request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return governorate.ToResponse();
    }
}

public sealed record SetGovernorateActiveCommand(
    GovernorateId Id,
    bool IsActive)
    : ICommand<GovernorateResponse>;

public sealed class SetGovernorateActiveCommandValidator :
    AbstractValidator<SetGovernorateActiveCommand>
{
    public SetGovernorateActiveCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(GovernorateId.Empty);
    }
}

public sealed class SetGovernorateActiveCommandHandler :
    ICommandHandler<SetGovernorateActiveCommand, GovernorateResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetGovernorateActiveCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GovernorateResponse>> Handle(
        SetGovernorateActiveCommand request,
        CancellationToken cancellationToken)
    {
        Governorate? governorate =
            await _dbContext.Governorates.SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);

        if (governorate is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            governorate.Activate();
        }
        else
        {
            governorate.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return governorate.ToResponse();
    }
}

public sealed record GetActiveGovernoratesQuery()
    : IQuery<IReadOnlyList<GovernoratePublicItem>>;

public sealed class GetActiveGovernoratesQueryHandler :
    IQueryHandler<
        GetActiveGovernoratesQuery,
        IReadOnlyList<GovernoratePublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveGovernoratesQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<GovernoratePublicItem>>> Handle(
        GetActiveGovernoratesQuery request,
        CancellationToken cancellationToken)
    {
        List<Governorate> governorates =
            await _dbContext.Governorates
                .AsNoTracking()
                .Where(governorate => governorate.IsActive)
                .OrderBy(governorate => governorate.EnglishName)
                .ToListAsync(cancellationToken);

        IReadOnlyList<GovernoratePublicItem> items =
            governorates
                .Select(governorate => governorate.ToPublicItem())
                .ToList();

        return Result<IReadOnlyList<GovernoratePublicItem>>.Success(items);
    }

    public sealed record GetAllGovernoratesQuery()
        : IQuery<IReadOnlyList<GovernorateResponse>>;

    public sealed class GetAllGovernoratesQueryHandler :
        IQueryHandler<GetAllGovernoratesQuery, IReadOnlyList<GovernorateResponse>>
    {
        private readonly ICaregiversDbContext _dbContext;

        public GetAllGovernoratesQueryHandler(ICaregiversDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result<IReadOnlyList<GovernorateResponse>>> Handle(
            GetAllGovernoratesQuery request,
            CancellationToken cancellationToken)
        {
            List<Governorate> governorates =
                await _dbContext.Governorates
                    .AsNoTracking()
                    .OrderBy(governorate => governorate.ArabicName)
                    .ToListAsync(cancellationToken);

            IReadOnlyList<GovernorateResponse> items =
                governorates
                    .Select(governorate => governorate.ToResponse())
                    .ToList();

            return Result<IReadOnlyList<GovernorateResponse>>.Success(items);
        }
    }
}