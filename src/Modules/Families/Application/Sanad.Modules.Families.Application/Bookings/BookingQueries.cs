using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Bookings;

public enum BookingTab
{
    Upcoming = 1,
    Current = 2,
    Past = 3
}

/// <summary>
/// The senior on a booking. The members are additive and nullable, and every
/// one of them is read from the stored dependent row:
/// <para>
/// <c>HasPhoto</c> says the dependent has a stored photo — the family reads
/// the bytes from <c>GET /api/v1/family/dependents/{elderlyId}/photo</c>;
/// no photo URL is projected here because the caregiver audience has no route
/// to the bytes.
/// </para>
/// <para>
/// <c>EmergencyContact</c> is the dependent's stored emergency contact. It is
/// populated on the family booking detail only — the family may already read
/// the same contact on
/// <c>GET /api/v1/family/dependents/{dependentId}/emergency-contact</c> — and
/// it is always <c>null</c> on the caregiver booking detail: no emergency
/// contact access or status model exists, so none is invented here.
/// </para>
/// </summary>
public sealed record BookingElderlySummaryResponse(
    Guid ElderlyId,
    string? ArabicFullName,
    string? EnglishFullName,
    Gender? Gender,
    DateOnly? DateOfBirth,
    string? HealthNotes,
    EmergencyContactResponse? EmergencyContact = null,
    bool HasPhoto = false);

public sealed record BookingDetailResponse(
    Guid Id,
    Guid FamilyId,
    Guid CreatedByUserId,
    Guid CaregiverId,
    BookingCaregiverType CaregiverType,
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
    DateTime? CancelledOnUtc,
    DateTime? RefundedOnUtc,
    BookingRefundState RefundState,
    // F6 caregiver-detail additions. They are additive and default to null so
    // every existing construction keeps its meaning; only the caregiver detail
    // read populates them from the persisted cancellation fact, so no other
    // detail response gains a value and no existing field changes.
    BookingCancellationActorSide? CancellationActorSide = null,
    BookingCancellationAction? CancellationAction = null,
    string? CancellationReasonCategory = null,
    BookingRefundEntitlement? RefundEntitlement = null,
    BookingRefundDecisionReason? RefundDecisionReason = null,
    // Caregiver-detail record additions (nullable, additive). Only the
    // caregiver booking detail read populates them, from the real stored
    // records; the family booking detail read leaves them null.
    IReadOnlyList<CaregiverBookingVisitHistoryItem>? VisitHistory = null,
    IReadOnlyList<CaregiverBookingListItemResponse>? UpcomingVisits = null,
    BookingReviewResponse? FamilyReview = null);

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
                                                b.Status == BookingStatus.Expired ||
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
                null,
                null,
                null,
                elderly?.ArabicFullName.Value,
                elderly?.EnglishFullName.Value,
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
                elderly.HealthNotes,
                // The family may already read the same contact on
                // GET /api/v1/family/dependents/{dependentId}/emergency-contact,
                // so projecting it here adds no new access.
                GetFamilyEmergencyContactQueryHandler.Map(elderly),
                !string.IsNullOrWhiteSpace(elderly.ProfileImageKey))
            : new BookingElderlySummaryResponse(
                booking.ElderlyId.Value,
                null,
                null,
                null,
                null,
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
            booking.CancelledOnUtc,
            booking.RefundedOnUtc,
            BookingRefundStates.Resolve(booking));

        return Result<BookingDetailResponse>.Success(response);
    }
}