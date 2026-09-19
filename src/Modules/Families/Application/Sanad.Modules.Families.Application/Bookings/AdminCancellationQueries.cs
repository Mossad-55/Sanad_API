using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record AdminCancellationItemResponse(
    Guid BookingId,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    BookingStatus CurrentStatus,
    BookingCancellationActorSide ActorSide,
    Guid ActorUserId,
    BookingCancellationAction Action,
    BookingStatus StatusAtCancellation,
    DateTime CancelledOnUtc,
    string? ReasonCategory,
    string? ReasonNote,
    BookingRefundEntitlement RefundEntitlement,
    BookingRefundDecisionReason RefundDecisionReason,
    bool IsCaregiverIncident,
    BookingRefundState RefundState);

public sealed record PagedAdminCancellations(
    IReadOnlyList<AdminCancellationItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ListAdminCancellationsQuery(
    int Page,
    int PageSize,
    BookingCancellationActorSide? Actor = null)
    : IQuery<PagedAdminCancellations>;

public sealed class ListAdminCancellationsQueryHandler
    : IQueryHandler<ListAdminCancellationsQuery, PagedAdminCancellations>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListAdminCancellationsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedAdminCancellations>> Handle(
        ListAdminCancellationsQuery request,
        CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;

        IQueryable<BookingCancellationFact> query = _dbContext.BookingCancellationFacts.AsNoTracking();

        if (request.Actor is not null)
        {
            query = query.Where(f => f.ActorSide == request.Actor.Value);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<BookingCancellationFact> facts = await query
            .OrderByDescending(f => f.CancelledOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (facts.Count == 0)
        {
            return Result<PagedAdminCancellations>.Success(
                new PagedAdminCancellations([], page, pageSize, totalCount));
        }

        List<BookingId> bookingIds = facts.Select(f => f.BookingId).ToList();

        List<Booking> bookings = await _dbContext.Bookings
            .AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id))
            .ToListAsync(cancellationToken);

        Dictionary<BookingId, Booking> bookingsById = bookings.ToDictionary(b => b.Id);

        var items = facts.Select(f =>
        {
            bookingsById.TryGetValue(f.BookingId, out Booking? booking);

            // Booking is expected to exist due to FK; if missing, we still map fact fields and use NotApplicable for refund state.
            BookingRefundState refundState = booking is not null
                ? BookingRefundStates.ResolveWithFact(booking, f)
                : BookingRefundState.NotApplicable;

            DateOnly bookingDate = booking?.BookingDate ?? default;
            TimeOnly startTime = booking?.StartTime ?? default;
            TimeOnly endTime = booking?.EndTime ?? default;
            BookingShiftType shiftType = booking?.ShiftType ?? default;
            BookingStatus currentStatus = booking?.Status ?? f.StatusAtCancellation;

            return new AdminCancellationItemResponse(
                f.BookingId.Value,
                bookingDate,
                startTime,
                endTime,
                shiftType,
                currentStatus,
                f.ActorSide,
                f.ActorUserId.Value,
                f.Action,
                f.StatusAtCancellation,
                f.CancelledOnUtc,
                f.ReasonCategory?.ToString(),
                f.ReasonNote,
                f.RefundEntitlement,
                f.RefundDecisionReason,
                f.IsCaregiverIncident,
                refundState);
        }).ToList();

        return Result<PagedAdminCancellations>.Success(
            new PagedAdminCancellations(items, page, pageSize, totalCount));
    }
}
