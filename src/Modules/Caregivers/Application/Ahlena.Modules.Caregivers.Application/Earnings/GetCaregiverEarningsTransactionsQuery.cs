using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Ahlena.Modules.Caregivers.Application.Earnings;

public sealed record CaregiverEarningsTransactionResponse(
    Guid BookingId,
    DateTime BookingDate,
    decimal Amount,
    string Status,
    string? ServiceAddress);

public sealed record GetCaregiverEarningsTransactionsQuery(
    Guid CaregiverId,
    UserId ActorUserId,
    int Page = 1,
    int PageSize = 20) : IQuery<CaregiverEarningsTransactionResponse[]>;

public sealed class GetCaregiverEarningsTransactionsQueryHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb) : IQueryHandler<
    GetCaregiverEarningsTransactionsQuery,
    CaregiverEarningsTransactionResponse[]>
{
    public async Task<Result<CaregiverEarningsTransactionResponse[]>> Handle(
        GetCaregiverEarningsTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId) &&
                     c.UserId == request.ActorUserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverEarningsTransactionResponse[]>.Failure(
                new Error("Caregivers.NotFound", "Caregiver not found."));
        }

        var transactions = await familiesDb.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == caregiver.Id && b.Status == BookingStatus.Completed)
            .OrderByDescending(b => b.CreatedOnUtc)
            .Select(b => new CaregiverEarningsTransactionResponse(
                b.Id.Value,
                b.CreatedOnUtc,
                b.PriceSnapshot.BaseCaregiverFee,
                b.Status.ToString(),
                b.ServiceAddress))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return Result<CaregiverEarningsTransactionResponse[]>.Success(transactions);
    }
}
