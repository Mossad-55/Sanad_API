using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record AreaResponse(
    AreaId Id,
    CityId CityId,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record AreaPublicItem(
    AreaId Id,
    string ArabicName,
    string EnglishName);

internal static class AreaMappings
{
    public static AreaResponse ToResponse(this Area area) =>
        new(area.Id, area.CityId, area.ArabicName, area.EnglishName, area.IsActive);

    public static AreaPublicItem ToPublicItem(this Area area) =>
        new(area.Id, area.ArabicName, area.EnglishName);
}

public sealed record CreateAreaCommand(
    CityId CityId,
    string ArabicName,
    string EnglishName)
    : ICommand<AreaResponse>;

public sealed class CreateAreaCommandValidator : AbstractValidator<CreateAreaCommand>
{
    public CreateAreaCommandValidator()
    {
        RuleFor(c => c.CityId).NotEqual(CityId.Empty);

        RuleFor(c => c.ArabicName)
            .NotEmpty()
            .MaximumLength(Area.MaximumNameLength);

        RuleFor(c => c.EnglishName)
            .NotEmpty()
            .MaximumLength(Area.MaximumNameLength);
    }
}

public sealed class CreateAreaCommandHandler :
    ICommandHandler<CreateAreaCommand, AreaResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateAreaCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AreaResponse>> Handle(
        CreateAreaCommand request,
        CancellationToken cancellationToken)
    {
        City? city =
            await _dbContext.Cities.SingleOrDefaultAsync(
                c => c.Id == request.CityId,
                cancellationToken);

        if (city is null)
        {
            return LookupsErrors.ParentNotFound;
        }

        Governorate? governorate =
            await _dbContext.Governorates.SingleOrDefaultAsync(
                g => g.Id == city.GovernorateId,
                cancellationToken);

        if (governorate is null)
        {
            return LookupsErrors.ParentNotFound;
        }

        if (!city.IsActive || !governorate.IsActive)
        {
            return LookupsErrors.ParentNotActive;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Areas.AnyAsync(
                area =>
                    area.CityId == request.CityId &&
                    (area.ArabicName == arabicName ||
                     area.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Area area =
            Area.Create(
                request.CityId,
                request.ArabicName,
                request.EnglishName);

        _dbContext.Areas.Add(area);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return area.ToResponse();
    }
}

public sealed record RenameAreaCommand(
    AreaId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<AreaResponse>;

public sealed class RenameAreaCommandValidator : AbstractValidator<RenameAreaCommand>
{
    public RenameAreaCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AreaId.Empty);

        RuleFor(c => c.ArabicName)
            .NotEmpty()
            .MaximumLength(Area.MaximumNameLength);

        RuleFor(c => c.EnglishName)
            .NotEmpty()
            .MaximumLength(Area.MaximumNameLength);
    }
}

public sealed class RenameAreaCommandHandler :
    ICommandHandler<RenameAreaCommand, AreaResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameAreaCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AreaResponse>> Handle(
        RenameAreaCommand request,
        CancellationToken cancellationToken)
    {
        Area? area =
            await _dbContext.Areas.SingleOrDefaultAsync(
                a => a.Id == request.Id,
                cancellationToken);

        if (area is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Areas.AnyAsync(
                other =>
                    other.Id != request.Id &&
                    other.CityId == area.CityId &&
                    (other.ArabicName == arabicName ||
                     other.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        area.UpdateNames(request.ArabicName, request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return area.ToResponse();
    }
}

public sealed record SetAreaActiveCommand(
    AreaId Id,
    bool IsActive)
    : ICommand<AreaResponse>;

public sealed class SetAreaActiveCommandValidator : AbstractValidator<SetAreaActiveCommand>
{
    public SetAreaActiveCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(AreaId.Empty);
    }
}

public sealed class SetAreaActiveCommandHandler :
    ICommandHandler<SetAreaActiveCommand, AreaResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetAreaActiveCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AreaResponse>> Handle(
        SetAreaActiveCommand request,
        CancellationToken cancellationToken)
    {
        Area? area =
            await _dbContext.Areas.SingleOrDefaultAsync(
                a => a.Id == request.Id,
                cancellationToken);

        if (area is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            area.Activate();
        }
        else
        {
            area.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return area.ToResponse();
    }
}

public sealed record GetAllAreasQuery(CityId CityId)
    : IQuery<IReadOnlyList<AreaResponse>>;

public sealed class GetAllAreasQueryValidator : AbstractValidator<GetAllAreasQuery>
{
    public GetAllAreasQueryValidator()
    {
        RuleFor(q => q.CityId).NotEqual(CityId.Empty);
    }
}

public sealed class GetAllAreasQueryHandler :
    IQueryHandler<GetAllAreasQuery, IReadOnlyList<AreaResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllAreasQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AreaResponse>>> Handle(
        GetAllAreasQuery request,
        CancellationToken cancellationToken)
    {
        List<Area> areas =
            await _dbContext.Areas
                .AsNoTracking()
                .Where(area => area.CityId == request.CityId)
                .OrderBy(area => area.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AreaResponse>>.Success(
            areas.Select(area => area.ToResponse()).ToList());
    }
}

public sealed record GetActiveAreasQuery(CityId CityId)
    : IQuery<IReadOnlyList<AreaPublicItem>>;

public sealed class GetActiveAreasQueryValidator : AbstractValidator<GetActiveAreasQuery>
{
    public GetActiveAreasQueryValidator()
    {
        RuleFor(q => q.CityId).NotEqual(CityId.Empty);
    }
}

public sealed class GetActiveAreasQueryHandler :
    IQueryHandler<GetActiveAreasQuery, IReadOnlyList<AreaPublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveAreasQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<AreaPublicItem>>> Handle(
        GetActiveAreasQuery request,
        CancellationToken cancellationToken)
    {
        List<Area> areas =
            await (
                from area in _dbContext.Areas.AsNoTracking()
                join city in _dbContext.Cities
                    on area.CityId equals city.Id
                join governorate in _dbContext.Governorates
                    on city.GovernorateId equals governorate.Id
                where area.IsActive &&
                      city.IsActive &&
                      governorate.IsActive &&
                      area.CityId == request.CityId
                orderby area.EnglishName
                select area)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AreaPublicItem>>.Success(
            areas.Select(area => area.ToPublicItem()).ToList());
    }
}