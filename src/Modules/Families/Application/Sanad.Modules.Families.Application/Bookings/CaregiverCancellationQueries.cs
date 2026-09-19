using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Bookings;

// ----------------------------- Caregiver cancellation summary (admin) -----------------------------

public sealed record CaregiverCancellationItem(
    Guid BookingId,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    DateTime CancelledOnUtc,
    string? Reason);

public sealed record CaregiverCancellationSummaryResponse(
    int CancellationCount,
    IReadOnlyList<CaregiverCancellationItem> Recent);

public sealed record GetCaregiverCancellationSummaryQuery(CaregiverId CaregiverId)
    : IQuery<CaregiverCancellationSummaryResponse>;

public sealed class GetCaregiverCancellationSummaryQueryHandler
    : IQueryHandler<GetCaregiverCancellationSummaryQuery, CaregiverCancellationSummaryResponse>
{
    private const int RecentLimit = 5;

    private readonly IFamiliesDbContext _dbContext;

    public GetCaregiverCancellationSummaryQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverCancellationSummaryResponse>> Handle(
        GetCaregiverCancellationSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var baseQuery = from f in _dbContext.BookingCancellationFacts.AsNoTracking()
                        join b in _dbContext.Bookings.AsNoTracking() on f.BookingId equals b.Id
                        where b.CaregiverId == request.CaregiverId
                            && f.ActorSide == BookingCancellationActorSide.Caregiver
                            && f.Action == BookingCancellationAction.Cancel
                        select new { Fact = f, Booking = b };

        int count = await baseQuery.CountAsync(cancellationToken);

        var recent = await baseQuery
            .OrderByDescending(x => x.Fact.CancelledOnUtc)
            .Take(RecentLimit)
            .ToListAsync(cancellationToken);

        var items = recent
            .Select(x => new CaregiverCancellationItem(
                x.Booking.Id.Value,
                x.Booking.BookingDate,
                x.Booking.StartTime,
                x.Booking.EndTime,
                x.Booking.ShiftType,
                x.Fact.CancelledOnUtc,
                x.Fact.ReasonNote))
            .ToList();

        return Result<CaregiverCancellationSummaryResponse>.Success(
            new CaregiverCancellationSummaryResponse(
                count,
                items));
    }
}
