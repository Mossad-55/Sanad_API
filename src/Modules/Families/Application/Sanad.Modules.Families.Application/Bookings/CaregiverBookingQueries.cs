using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record CaregiverBookingListItemResponse(
    Guid Id,
    Guid FamilyId,
    Guid ElderlyId,
    string? SeniorArabicName,
    string? SeniorEnglishName,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency,
    string? CancellationReason,
    DateTime? CancelledOnUtc,
    BookingRefundState RefundState);

public sealed record GetCaregiverBookingsQuery(
    CaregiverId CaregiverId,
    BookingTab Tab) : IQuery<IReadOnlyList<CaregiverBookingListItemResponse>>;

public sealed class GetCaregiverBookingsQueryHandler
    : IQueryHandler<GetCaregiverBookingsQuery, IReadOnlyList<CaregiverBookingListItemResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCaregiverBookingsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<CaregiverBookingListItemResponse>>> Handle(
        GetCaregiverBookingsQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<Booking> query = _dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == request.CaregiverId);

        query = request.Tab switch
        {
            BookingTab.Upcoming => query.Where(b =>
                b.Status == BookingStatus.PendingCaregiverApproval ||
                b.Status == BookingStatus.Confirmed),
            BookingTab.Current => query.Where(b => b.Status == BookingStatus.InProgress),
            BookingTab.Past => query.Where(b =>
                b.Status == BookingStatus.Completed ||
                b.Status == BookingStatus.CancelledByFamily ||
                b.Status == BookingStatus.DeclinedByCaregiver ||
                b.Status == BookingStatus.CancelledByCaregiver ||
                b.Status == BookingStatus.Expired ||
                b.Status == BookingStatus.Refunded),
            _ => query
        };

        List<Booking> bookings = await query
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync(cancellationToken);

        var elderlyIds = bookings.Select(b => b.ElderlyId).Distinct().ToList();
        Dictionary<ElderlyId, Elderly> elderlies = await _dbContext.Elderlies
            .AsNoTracking()
            .Where(e => elderlyIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var items = bookings.Select(b =>
        {
            elderlies.TryGetValue(b.ElderlyId, out Elderly? elderly);
            return new CaregiverBookingListItemResponse(
                b.Id.Value,
                b.FamilyId.Value,
                b.ElderlyId.Value,
                elderly?.ArabicFullName.Value,
                elderly?.EnglishFullName.Value,
                b.BookingDate,
                b.StartTime,
                b.EndTime,
                b.ShiftType,
                b.Status,
                b.PriceSnapshot.TotalPayableAmount,
                b.PriceSnapshot.Currency,
                b.CancellationReason,
                b.CancelledOnUtc,
                BookingRefundStates.Resolve(b));
        }).ToList();

        return Result<IReadOnlyList<CaregiverBookingListItemResponse>>.Success(items);
    }
}

public sealed record GetCaregiverBookingDetailQuery(
    CaregiverId CaregiverId,
    BookingId BookingId) : IQuery<BookingDetailResponse>;

public sealed class GetCaregiverBookingDetailQueryHandler
    : IQueryHandler<GetCaregiverBookingDetailQuery, BookingDetailResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCaregiverBookingDetailQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BookingDetailResponse>> Handle(
        GetCaregiverBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId,
                cancellationToken);

        if (booking is null)
        {
            return Result<BookingDetailResponse>.Failure(
                new Error("Bookings.NotFound", "Booking not found for this caregiver."));
        }

        Elderly? elderly = await _dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == booking.ElderlyId, cancellationToken);

        return Result<BookingDetailResponse>.Success(
            BookingDetailMapper.ToResponse(booking, elderly));
    }
}

internal static class BookingDetailMapper
{
    public static BookingDetailResponse ToResponse(Booking booking, Elderly? elderly)
    {
        var elderlySummary = elderly is not null
            ? new BookingElderlySummaryResponse(
                elderly.Id.Value,
                elderly.ArabicFullName.Value,
                elderly.EnglishFullName.Value,
                elderly.Gender,
                elderly.DateOfBirth,
                elderly.HealthNotes)
            : new BookingElderlySummaryResponse(
                booking.ElderlyId.Value,
                null,
                null,
                null,
                null,
                null);

        return new BookingDetailResponse(
            booking.Id.Value,
            booking.FamilyId.Value,
            booking.CreatedByUserId.Value,
            booking.CaregiverId.Value,
            booking.CaregiverType,
            booking.ShiftType,
            booking.BookingDate,
            booking.StartTime,
            booking.EndTime,
            booking.ServiceAddress,
            booking.SpecialInstructions,
            booking.Status,
            booking.PriceSnapshot.BaseCaregiverFee,
            booking.PriceSnapshot.PlatformFeePercentage,
            booking.PriceSnapshot.PlatformFeeAmount,
            booking.PriceSnapshot.TotalPayableAmount,
            booking.PriceSnapshot.Currency,
            booking.CancellationReason,
            booking.CaregiverNotes,
            elderlySummary,
            booking.CreatedOnUtc,
            booking.PaidOnUtc,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            booking.CompletedOnUtc,
            booking.CancelledOnUtc,
            booking.RefundedOnUtc,
            BookingRefundStates.Resolve(booking));
    }
}
