using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record AdminBookingListItemResponse(
    Guid Id,
    Guid FamilyId,
    Guid CaregiverId,
    Guid ElderlyId,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency,
    BookingRefundState RefundState,
    DateTime? PaidOnUtc,
    DateTime? CancelledOnUtc,
    DateTime? RefundedOnUtc,
    string? CancellationReason);

public sealed record PagedAdminBookings(
    IReadOnlyList<AdminBookingListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ListAdminBookingsQuery(
    int Page,
    int PageSize,
    AdminBookingFinanceFilter Finance = AdminBookingFinanceFilter.All)
    : IQuery<PagedAdminBookings>;

public sealed class ListAdminBookingsQueryHandler
    : IQueryHandler<ListAdminBookingsQuery, PagedAdminBookings>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListAdminBookingsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedAdminBookings>> Handle(
        ListAdminBookingsQuery request,
        CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;

        IQueryable<Booking> query = _dbContext.Bookings.AsNoTracking();

        query = request.Finance switch
        {
            AdminBookingFinanceFilter.Cancelled => query.Where(b =>
                b.Status == BookingStatus.CancelledByFamily
                || b.Status == BookingStatus.DeclinedByCaregiver
                || b.Status == BookingStatus.CancelledByCaregiver),
            AdminBookingFinanceFilter.FailedRefund => query.Where(b =>
                b.PaidOnUtc != null
                && b.RefundedOnUtc == null
                && (b.Status == BookingStatus.CancelledByFamily
                    || b.Status == BookingStatus.DeclinedByCaregiver
                    || b.Status == BookingStatus.CancelledByCaregiver
                    || b.Status == BookingStatus.Expired)),
            AdminBookingFinanceFilter.Refunded => query.Where(b =>
                b.Status == BookingStatus.Refunded),
            _ => query.Where(b =>
                b.Status == BookingStatus.CancelledByFamily
                || b.Status == BookingStatus.DeclinedByCaregiver
                || b.Status == BookingStatus.CancelledByCaregiver
                || b.Status == BookingStatus.Refunded
                || b.Status == BookingStatus.Expired)
        };

        int totalCount = await query.CountAsync(cancellationToken);

        List<Booking> bookings = await query
            .OrderByDescending(b => b.CancelledOnUtc ?? b.RefundedOnUtc ?? b.UpdatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = bookings.Select(b => new AdminBookingListItemResponse(
            b.Id.Value,
            b.FamilyId.Value,
            b.CaregiverId.Value,
            b.ElderlyId.Value,
            b.BookingDate,
            b.StartTime,
            b.EndTime,
            b.ShiftType,
            b.Status,
            b.PriceSnapshot.TotalPayableAmount,
            b.PriceSnapshot.Currency,
            BookingRefundStates.Resolve(b),
            b.PaidOnUtc,
            b.CancelledOnUtc,
            b.RefundedOnUtc,
            b.CancellationReason)).ToList();

        return Result<PagedAdminBookings>.Success(
            new PagedAdminBookings(items, page, pageSize, totalCount));
    }
}

public sealed record GetAdminBookingDetailQuery(BookingId BookingId)
    : IQuery<BookingDetailResponse>;

public sealed class GetAdminBookingDetailQueryHandler
    : IQueryHandler<GetAdminBookingDetailQuery, BookingDetailResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetAdminBookingDetailQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BookingDetailResponse>> Handle(
        GetAdminBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

        if (booking is null)
        {
            return Result<BookingDetailResponse>.Failure(
                new Error("Bookings.NotFound", "Booking not found."));
        }

        Elderly? elderly = await _dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == booking.ElderlyId, cancellationToken);

        return Result<BookingDetailResponse>.Success(
            BookingDetailMapper.ToResponse(booking, elderly));
    }
}
