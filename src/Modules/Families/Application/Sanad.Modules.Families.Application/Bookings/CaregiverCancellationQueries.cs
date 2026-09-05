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
        List<Booking> recent = await _dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == request.CaregiverId
                && b.Status == BookingStatus.CancelledByCaregiver)
            .OrderByDescending(b => b.CancelledOnUtc)
            .Take(RecentLimit)
            .ToListAsync(cancellationToken);

        int count = await _dbContext.Bookings
            .AsNoTracking()
            .CountAsync(
                b => b.CaregiverId == request.CaregiverId
                    && b.Status == BookingStatus.CancelledByCaregiver,
                cancellationToken);

        return Result<CaregiverCancellationSummaryResponse>.Success(
            new CaregiverCancellationSummaryResponse(
                count,
                recent
                    .Select(b => new CaregiverCancellationItem(
                        b.Id.Value,
                        b.BookingDate,
                        b.StartTime,
                        b.EndTime,
                        b.ShiftType,
                        b.CancelledOnUtc!.Value,
                        b.CancellationReason))
                    .ToList()));
    }
}