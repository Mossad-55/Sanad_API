using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Bookings;

public enum BookingTab
{
    Upcoming = 1,
    Current = 2,
    Past = 3
}

public sealed record BookingElderlySummaryResponse(
    Guid ElderlyId,
    string ArabicFullName,
    string EnglishFullName,
    Gender Gender,
    DateOnly DateOfBirth,
    string? HealthNotes);

public sealed record BookingDetailResponse(
    Guid Id,
    Guid FamilyId,
    Guid CreatedByUserId,
    Guid CaregiverId,
    CaregiverType CaregiverType,
    BookingShiftType ShiftType,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ServiceAddress,
    string? SpecialInstructions,
    BookingStatus Status,
    decimal BaseCaregiverFee,
    decimal PlatformFeePercentage,
    decimal PlatformFeeAmount,
    decimal TotalPayableAmount,
    string Currency,
    string? CancellationReason,
    string? CaregiverNotes,
    BookingElderlySummaryResponse Elderly,
    DateTime CreatedOnUtc,
    DateTime? PaidOnUtc,
    DateTime? ConfirmedOnUtc,
    DateTime? StartedOnUtc,
    DateTime? CompletedOnUtc,
    DateTime? CancelledOnUtc);

// ============================= Family Queries =============================

public sealed record GetFamilyBookingsQuery(
    UserId UserId,
    BookingTab Tab) : IQuery<IReadOnlyList<FamilyBookingListItemResponse>>;

public sealed class GetFamilyBookingsQueryHandler : IQueryHandler<GetFamilyBookingsQuery, IReadOnlyList<FamilyBookingListItemResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyBookingsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<FamilyBookingListItemResponse>>> Handle(
        GetFamilyBookingsQuery request,
        CancellationToken cancellationToken)
    {
        var family = await _dbContext.Families
            .AsNoTracking()
            .Include(f => f.Members)
            .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

        if (family is null)
        {
            return Result<IReadOnlyList<FamilyBookingListItemResponse>>.Failure(
                new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
        }

        IQueryable<Booking> query = _dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.FamilyId == family.Id);

        query = request.Tab switch
        {
            BookingTab.Upcoming => query.Where(b => b.Status == BookingStatus.PendingPayment ||
                                                    b.Status == BookingStatus.PendingCaregiverApproval ||
                                                    b.Status == BookingStatus.Confirmed),
            BookingTab.Current => query.Where(b => b.Status == BookingStatus.InProgress),
            BookingTab.Past => query.Where(b => b.Status == BookingStatus.Completed ||
                                                b.Status == BookingStatus.CancelledByFamily ||
                                                b.Status == BookingStatus.DeclinedByCaregiver ||
                                                b.Status == BookingStatus.CancelledByCaregiver ||
                                                b.Status == BookingStatus.Refunded),
            _ => query
        };

        var bookings = await query
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync(cancellationToken);

        var elderlyIds = bookings.Select(b => b.ElderlyId).Distinct().ToList();
        var elderlies = await _dbContext.Elderlies
            .AsNoTracking()
            .Where(e => elderlyIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var items = bookings.Select(b =>
        {
            elderlies.TryGetValue(b.ElderlyId, out var elderly);
            return new FamilyBookingListItemResponse(
                b.Id.Value,
                b.CaregiverId.Value,
                string.Empty,
                string.Empty,
                null,
                elderly?.ArabicFullName.Value ?? string.Empty,
                elderly?.EnglishFullName.Value ?? string.Empty,
                b.BookingDate,
                b.StartTime,
                b.EndTime,
                b.ShiftType,
                b.Status,
                b.PriceSnapshot.TotalPayableAmount,
                b.PriceSnapshot.Currency);
        }).ToList();

        return Result<IReadOnlyList<FamilyBookingListItemResponse>>.Success(items);
    }
}

public sealed record GetFamilyBookingDetailQuery(
    UserId UserId,
    BookingId BookingId) : IQuery<BookingDetailResponse>;

public sealed class GetFamilyBookingDetailQueryHandler : IQueryHandler<GetFamilyBookingDetailQuery, BookingDetailResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyBookingDetailQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BookingDetailResponse>> Handle(
        GetFamilyBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        var family = await _dbContext.Families
            .AsNoTracking()
            .Include(f => f.Members)
            .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

        if (family is null)
        {
            return Result<BookingDetailResponse>.Failure(
                new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
        }

        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.FamilyId == family.Id, cancellationToken);

        if (booking is null)
        {
            return Result<BookingDetailResponse>.Failure(
                new Error("Bookings.NotFound", "Booking was not found in this family."));
        }

        Elderly? elderly = await _dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == booking.ElderlyId, cancellationToken);

        var elderlySummary = elderly != null
            ? new BookingElderlySummaryResponse(
                elderly.Id.Value,
                elderly.ArabicFullName.Value,
                elderly.EnglishFullName.Value,
                elderly.Gender,
                elderly.DateOfBirth,
                elderly.HealthNotes)
            : new BookingElderlySummaryResponse(
                booking.ElderlyId.Value,
                string.Empty,
                string.Empty,
                Gender.Male,
                DateOnly.MinValue,
                null);

        var response = new BookingDetailResponse(
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
            booking.CancelledOnUtc);

        return Result<BookingDetailResponse>.Success(response);
    }
}