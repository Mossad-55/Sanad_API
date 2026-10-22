using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

/// <summary>
/// Stable admin catalog error codes. Domain guards throw
/// <see cref="DomainException"/>; the application layer maps them to these
/// codes so the API keeps a stable contract (the Finance seam split).
/// </summary>
public static class RentalCatalogErrors
{
    public static readonly Error NotFound =
        new("Rentals.Catalog.NotFound", "The rental equipment item was not found.");

    public static readonly Error Invalid =
        new("Rentals.Catalog.Invalid", "The rental equipment item is not valid.");

    public static readonly Error Unknown =
        new("Rentals.Catalog.Unknown", "The rental equipment operation could not be completed.");
}

public sealed record RentalEquipmentAdminResponse(
    Guid Id,
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    decimal DailyRate,
    decimal Deposit,
    int MaximumRentalDays,
    bool IsActive,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record CreateRentalEquipmentCommand(
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    long DailyRateMinorUnits,
    long DepositMinorUnits,
    int MaximumRentalDays,
    DateTime UtcNow) : ICommand<Guid>;

public sealed record UpdateRentalEquipmentCommand(
    Guid EquipmentId,
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    long DailyRateMinorUnits,
    long DepositMinorUnits,
    int MaximumRentalDays,
    DateTime UtcNow) : ICommand;

public sealed record SetRentalEquipmentActiveCommand(Guid EquipmentId, bool IsActive, DateTime UtcNow) : ICommand;

public sealed record ListAdminRentalEquipmentQuery : IQuery<IReadOnlyList<RentalEquipmentAdminResponse>>;

public sealed class CreateRentalEquipmentCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<CreateRentalEquipmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRentalEquipmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalEquipment equipment = RentalEquipment.Create(
                request.NameEnglish,
                request.NameArabic,
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.DailyRateMinorUnits,
                request.DepositMinorUnits,
                request.MaximumRentalDays,
                request.UtcNow);

            dbContext.RentalEquipment.Add(equipment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(equipment.Id);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(new Error(RentalCatalogErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<Guid>.Failure(RentalCatalogErrors.Unknown);
        }
    }
}

public sealed class UpdateRentalEquipmentCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<UpdateRentalEquipmentCommand>
{
    public async Task<Result> Handle(UpdateRentalEquipmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalEquipment? equipment = await dbContext.RentalEquipment
                .SingleOrDefaultAsync(item => item.Id == request.EquipmentId, cancellationToken);
            if (equipment is null)
                return Result.Failure(RentalCatalogErrors.NotFound);

            equipment.Update(
                request.NameEnglish,
                request.NameArabic,
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.DailyRateMinorUnits,
                request.DepositMinorUnits,
                request.MaximumRentalDays,
                request.UtcNow);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalCatalogErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalCatalogErrors.Unknown);
        }
    }
}

public sealed class SetRentalEquipmentActiveCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<SetRentalEquipmentActiveCommand>
{
    public async Task<Result> Handle(SetRentalEquipmentActiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalEquipment? equipment = await dbContext.RentalEquipment
                .SingleOrDefaultAsync(item => item.Id == request.EquipmentId, cancellationToken);
            if (equipment is null)
                return Result.Failure(RentalCatalogErrors.NotFound);

            if (request.IsActive)
                equipment.Activate(request.UtcNow);
            else
                equipment.Deactivate(request.UtcNow);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalCatalogErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalCatalogErrors.Unknown);
        }
    }
}

public sealed class ListAdminRentalEquipmentQueryHandler(IRentalsDbContext dbContext)
    : IQueryHandler<ListAdminRentalEquipmentQuery, IReadOnlyList<RentalEquipmentAdminResponse>>
{
    public async Task<Result<IReadOnlyList<RentalEquipmentAdminResponse>>> Handle(
        ListAdminRentalEquipmentQuery request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.RentalEquipment
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return items.Select(Map).ToList();
    }

    internal static RentalEquipmentAdminResponse Map(RentalEquipment item) => new(
        item.Id,
        item.NameEnglish,
        item.NameArabic,
        item.DescriptionEnglish,
        item.DescriptionArabic,
        ToMajor(item.DailyRateMinorUnits),
        ToMajor(item.DepositMinorUnits),
        item.MaximumRentalDays,
        item.IsActive,
        item.CreatedOnUtc,
        item.UpdatedOnUtc);

    internal static decimal ToMajor(long minorUnits) => minorUnits / 100m;
}
