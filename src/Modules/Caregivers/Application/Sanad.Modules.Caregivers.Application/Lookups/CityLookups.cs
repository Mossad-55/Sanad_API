using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record CityResponse(
    CityId Id,
    GovernorateId GovernorateId,
    string ArabicName,
    string EnglishName,
    bool IsActive);

public sealed record CityPublicItem(
    CityId Id,
    string ArabicName,
    string EnglishName);

internal static class CityMappings
{
    public static CityResponse ToResponse(this City city) =>
        new(city.Id, city.GovernorateId, city.ArabicName, city.EnglishName, city.IsActive);

    public static CityPublicItem ToPublicItem(this City city) =>
        new(city.Id, city.ArabicName, city.EnglishName);
}

public sealed record CreateCityCommand(
    GovernorateId GovernorateId,
    string ArabicName,
    string EnglishName)
    : ICommand<CityResponse>;

public sealed class CreateCityCommandValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityCommandValidator()
    {
        RuleFor(c => c.GovernorateId)
            .NotEqual(GovernorateId.Empty);

        RuleFor(c => c.ArabicName)
            .NotEmpty()
            .MaximumLength(City.MaximumNameLength);

        RuleFor(c => c.EnglishName)
            .NotEmpty()
            .MaximumLength(City.MaximumNameLength);
    }
}

public sealed class CreateCityCommandHandler :
    ICommandHandler<CreateCityCommand, CityResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateCityCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CityResponse>> Handle(
        CreateCityCommand request,
        CancellationToken cancellationToken)
    {
        Governorate? governorate =
            await _dbContext.Governorates.SingleOrDefaultAsync(
                g => g.Id == request.GovernorateId,
                cancellationToken);

        if (governorate is null)
        {
            return LookupsErrors.ParentNotFound;
        }

        if (!governorate.IsActive)
        {
            return LookupsErrors.ParentNotActive;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Cities.AnyAsync(
                city =>
                    city.GovernorateId == request.GovernorateId &&
                    (city.ArabicName == arabicName ||
                     city.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        City city =
            City.Create(
                request.GovernorateId,
                request.ArabicName,
                request.EnglishName);

        _dbContext.Cities.Add(city);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return city.ToResponse();
    }
}

public sealed record RenameCityCommand(
    CityId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<CityResponse>;

public sealed class RenameCityCommandValidator : AbstractValidator<RenameCityCommand>
{
    public RenameCityCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(CityId.Empty);

        RuleFor(c => c.ArabicName)
            .NotEmpty()
            .MaximumLength(City.MaximumNameLength);

        RuleFor(c => c.EnglishName)
            .NotEmpty()
            .MaximumLength(City.MaximumNameLength);
    }
}

public sealed class RenameCityCommandHandler :
    ICommandHandler<RenameCityCommand, CityResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameCityCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CityResponse>> Handle(
        RenameCityCommand request,
        CancellationToken cancellationToken)
    {
        City? city =
            await _dbContext.Cities.SingleOrDefaultAsync(
                c => c.Id == request.Id,
                cancellationToken);

        if (city is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName = request.ArabicName.Trim();
        string englishName = request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Cities.AnyAsync(
                other =>
                    other.Id != request.Id &&
                    other.GovernorateId == city.GovernorateId &&
                    (other.ArabicName == arabicName ||
                     other.EnglishName == englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        city.UpdateNames(request.ArabicName, request.EnglishName);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return city.ToResponse();
    }
}

public sealed record SetCityActiveCommand(
    CityId Id,
    bool IsActive)
    : ICommand<CityResponse>;

public sealed class SetCityActiveCommandValidator : AbstractValidator<SetCityActiveCommand>
{
    public SetCityActiveCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(CityId.Empty);
    }
}

public sealed class SetCityActiveCommandHandler :
    ICommandHandler<SetCityActiveCommand, CityResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetCityActiveCommandHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CityResponse>> Handle(
        SetCityActiveCommand request,
        CancellationToken cancellationToken)
    {
        City? city =
            await _dbContext.Cities.SingleOrDefaultAsync(
                c => c.Id == request.Id,
                cancellationToken);

        if (city is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            city.Activate();
        }
        else
        {
            city.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return city.ToResponse();
    }
}

public sealed record GetAllCitiesQuery(GovernorateId GovernorateId)
    : IQuery<IReadOnlyList<CityResponse>>;

public sealed class GetAllCitiesQueryValidator : AbstractValidator<GetAllCitiesQuery>
{
    public GetAllCitiesQueryValidator()
    {
        RuleFor(q => q.GovernorateId).NotEqual(GovernorateId.Empty);
    }
}

public sealed class GetAllCitiesQueryHandler :
    IQueryHandler<GetAllCitiesQuery, IReadOnlyList<CityResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllCitiesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<CityResponse>>> Handle(
        GetAllCitiesQuery request,
        CancellationToken cancellationToken)
    {
        List<City> cities =
            await _dbContext.Cities
                .AsNoTracking()
                .Where(city => city.GovernorateId == request.GovernorateId)
                .OrderBy(city => city.EnglishName)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CityResponse>>.Success(
            cities.Select(city => city.ToResponse()).ToList());
    }
}

public sealed record GetActiveCitiesQuery(GovernorateId GovernorateId)
    : IQuery<IReadOnlyList<CityPublicItem>>;

public sealed class GetActiveCitiesQueryValidator : AbstractValidator<GetActiveCitiesQuery>
{
    public GetActiveCitiesQueryValidator()
    {
        RuleFor(q => q.GovernorateId).NotEqual(GovernorateId.Empty);
    }
}

public sealed class GetActiveCitiesQueryHandler :
    IQueryHandler<GetActiveCitiesQuery, IReadOnlyList<CityPublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveCitiesQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<CityPublicItem>>> Handle(
        GetActiveCitiesQuery request,
        CancellationToken cancellationToken)
    {
        List<City> cities =
            await (
                from city in _dbContext.Cities.AsNoTracking()
                join governorate in _dbContext.Governorates
                    on city.GovernorateId equals governorate.Id
                where city.IsActive &&
                      governorate.IsActive &&
                      city.GovernorateId == request.GovernorateId
                orderby city.EnglishName
                select city)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CityPublicItem>>.Success(
            cities.Select(city => city.ToPublicItem()).ToList());
    }
}