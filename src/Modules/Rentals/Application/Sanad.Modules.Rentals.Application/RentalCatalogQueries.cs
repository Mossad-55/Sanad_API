using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

/// <summary>
/// The pre-purchase catalog read (clause 8: rate, deposit and rental-length
/// terms "display before purchase"; decision #19). Inactive items never
/// appear. Amounts cross the wire as EGP major units; the domain keeps the
/// exact piastre integers.
/// </summary>
public sealed record RentalCatalogItemResponse(
    Guid Id,
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    decimal DailyRate,
    decimal Deposit,
    int MaximumRentalDays);

public sealed record GetRentalCatalogQuery : IQuery<IReadOnlyList<RentalCatalogItemResponse>>;

public sealed record GetRentalCatalogItemQuery(Guid EquipmentId) : IQuery<RentalCatalogItemResponse?>;

public static class RentalCatalogReadErrors
{
    public static readonly Error NotFound =
        new("Rentals.Catalog.NotFound", "The rental equipment item was not found.");
}

public sealed class GetRentalCatalogQueryHandler(IRentalsDbContext dbContext)
    : IQueryHandler<GetRentalCatalogQuery, IReadOnlyList<RentalCatalogItemResponse>>
{
    public async Task<Result<IReadOnlyList<RentalCatalogItemResponse>>> Handle(
        GetRentalCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.RentalEquipment
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.NameEnglish)
            .ToListAsync(cancellationToken);

        return items.Select(Map).ToList();
    }

    internal static RentalCatalogItemResponse Map(RentalEquipment item) => new(
        item.Id,
        item.NameEnglish,
        item.NameArabic,
        item.DescriptionEnglish,
        item.DescriptionArabic,
        item.DailyRateMinorUnits / 100m,
        item.DepositMinorUnits / 100m,
        item.MaximumRentalDays);
}

public sealed class GetRentalCatalogItemQueryHandler(IRentalsDbContext dbContext)
    : IQueryHandler<GetRentalCatalogItemQuery, RentalCatalogItemResponse?>
{
    public async Task<Result<RentalCatalogItemResponse?>> Handle(
        GetRentalCatalogItemQuery request,
        CancellationToken cancellationToken)
    {
        RentalEquipment? item = await dbContext.RentalEquipment
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.EquipmentId, cancellationToken);

        if (item is null || !item.IsActive)
            return Result<RentalCatalogItemResponse?>.Failure(RentalCatalogReadErrors.NotFound);

        return Result<RentalCatalogItemResponse?>.Success(GetRentalCatalogQueryHandler.Map(item));
    }
}
