using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Lookups;

public sealed record ServiceResponse(
    ServiceId Id,
    string ArabicName,
    string EnglishName,
    string IconPath,
    CaregiverType CaregiverType,
    bool IsActive);

public sealed record ServicePublicItem(
    ServiceId Id,
    string ArabicName,
    string EnglishName,
    string IconPath,
    CaregiverType CaregiverType);

internal static class ServiceMappings
{
    public static ServiceResponse ToResponse(
        this Service service)
    {
        return new ServiceResponse(
            service.Id,
            service.ArabicName,
            service.EnglishName,
            service.IconPath,
            service.CaregiverType,
            service.IsActive);
    }

    public static ServicePublicItem ToPublicItem(
        this Service service)
    {
        return new ServicePublicItem(
            service.Id,
            service.ArabicName,
            service.EnglishName,
            service.IconPath,
            service.CaregiverType);
    }
}

public sealed record CreateServiceCommand(
    string ArabicName,
    string EnglishName,
    string IconPath,
    CaregiverType CaregiverType,
    bool IsActive)
    : ICommand<ServiceResponse>;

public sealed class CreateServiceCommandValidator :
    AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator()
    {
        RuleFor(command =>
                command.ArabicName)
            .NotEmpty()
            .MaximumLength(
                Service.MaximumNameLength);

        RuleFor(command =>
                command.EnglishName)
            .NotEmpty()
            .MaximumLength(
                Service.MaximumNameLength);

        RuleFor(command =>
                command.IconPath)
            .NotEmpty()
            .MaximumLength(
                Service.MaximumIconPathLength);

        RuleFor(command =>
                command.CaregiverType)
            .IsInEnum();
    }
}

public sealed class CreateServiceCommandHandler :
    ICommandHandler<
        CreateServiceCommand,
        ServiceResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public CreateServiceCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ServiceResponse>> Handle(
        CreateServiceCommand request,
        CancellationToken cancellationToken)
    {
        string arabicName =
            request.ArabicName.Trim();

        string englishName =
            request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Services.AnyAsync(
                service =>
                    service.CaregiverType ==
                        request.CaregiverType &&
                    (service.ArabicName ==
                        arabicName ||
                     service.EnglishName ==
                        englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        Service service =
            Service.Create(
                request.ArabicName,
                request.EnglishName,
                request.IconPath,
                request.CaregiverType,
                request.IsActive);

        _dbContext.Services.Add(
            service);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return service.ToResponse();
    }
}

public sealed record RenameServiceCommand(
    ServiceId Id,
    string ArabicName,
    string EnglishName)
    : ICommand<ServiceResponse>;

public sealed class RenameServiceCommandValidator :
    AbstractValidator<RenameServiceCommand>
{
    public RenameServiceCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(ServiceId.Empty);

        RuleFor(command =>
                command.ArabicName)
            .NotEmpty()
            .MaximumLength(
                Service.MaximumNameLength);

        RuleFor(command =>
                command.EnglishName)
            .NotEmpty()
            .MaximumLength(
                Service.MaximumNameLength);
    }
}

public sealed class RenameServiceCommandHandler :
    ICommandHandler<
        RenameServiceCommand,
        ServiceResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RenameServiceCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ServiceResponse>> Handle(
        RenameServiceCommand request,
        CancellationToken cancellationToken)
    {
        Service? service =
            await _dbContext.Services
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == request.Id,
                    cancellationToken);

        if (service is null)
        {
            return LookupsErrors.NotFound;
        }

        string arabicName =
            request.ArabicName.Trim();

        string englishName =
            request.EnglishName.Trim();

        bool duplicate =
            await _dbContext.Services.AnyAsync(
                item =>
                    item.Id != request.Id &&
                    item.CaregiverType ==
                        service.CaregiverType &&
                    (item.ArabicName ==
                        arabicName ||
                     item.EnglishName ==
                        englishName),
                cancellationToken);

        if (duplicate)
        {
            return LookupsErrors.NameAlreadyInUse;
        }

        service.UpdateNames(
            request.ArabicName,
            request.EnglishName);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return service.ToResponse();
    }
}

public sealed record SetServiceActiveCommand(
    ServiceId Id,
    bool IsActive)
    : ICommand<ServiceResponse>;

public sealed class SetServiceActiveCommandValidator :
    AbstractValidator<SetServiceActiveCommand>
{
    public SetServiceActiveCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(ServiceId.Empty);
    }
}

public sealed class SetServiceActiveCommandHandler :
    ICommandHandler<
        SetServiceActiveCommand,
        ServiceResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public SetServiceActiveCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ServiceResponse>> Handle(
        SetServiceActiveCommand request,
        CancellationToken cancellationToken)
    {
        Service? service =
            await _dbContext.Services
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == request.Id,
                    cancellationToken);

        if (service is null)
        {
            return LookupsErrors.NotFound;
        }

        if (request.IsActive)
        {
            service.Activate();
        }
        else
        {
            service.Deactivate();
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return service.ToResponse();
    }
}

public sealed record GetActiveServicesQuery()
    : IQuery<IReadOnlyList<ServicePublicItem>>;

public sealed class GetActiveServicesQueryHandler :
    IQueryHandler<
        GetActiveServicesQuery,
        IReadOnlyList<ServicePublicItem>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetActiveServicesQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ServicePublicItem>>> Handle(
        GetActiveServicesQuery request,
        CancellationToken cancellationToken)
    {
        List<Service> services =
            await _dbContext.Services
                .AsNoTracking()
                .Where(service =>
                    service.IsActive)
                .OrderBy(service =>
                    service.ArabicName)
                .ToListAsync(
                    cancellationToken);

        IReadOnlyList<ServicePublicItem> items =
            services
                .Select(service =>
                    service.ToPublicItem())
                .ToList();

        return Result<IReadOnlyList<ServicePublicItem>>
            .Success(items);
    }
}
public sealed record GetAllServicesQuery()
    : IQuery<IReadOnlyList<ServiceResponse>>;

public sealed class GetAllServicesQueryHandler :
    IQueryHandler<GetAllServicesQuery, IReadOnlyList<ServiceResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetAllServicesQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ServiceResponse>>> Handle(
        GetAllServicesQuery request,
        CancellationToken cancellationToken)
    {
        List<Service> services =
            await _dbContext.Services
                .AsNoTracking()
                .OrderBy(service => service.CaregiverType)
                .ThenBy(service => service.ArabicName)
                .ToListAsync(cancellationToken);

        IReadOnlyList<ServiceResponse> items =
            services.Select(
                service => service.ToResponse())
                .ToList();

        return Result<IReadOnlyList<ServiceResponse>>.Success(items);
    }
}