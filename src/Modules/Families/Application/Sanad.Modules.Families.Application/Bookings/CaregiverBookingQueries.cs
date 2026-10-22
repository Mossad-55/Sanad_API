using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Reports;

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

/// <summary>
/// One real report row recorded for a booking: the caregiver's visit report
/// (at most one per booking) or the medical report (medical caregivers only).
/// Every value is read from the stored row; the report type is the stored
/// kind (<c>visit</c> or <c>medical</c>) and the fields of the other kind stay
/// <c>null</c>. <c>PhotoAvailable</c> says the medical report carries a photo;
/// the photo bytes are read through
/// <c>GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo</c>.
/// </summary>
public sealed record CaregiverBookingVisitHistoryItem(
    Guid ReportId,
    string ReportType,
    BookingCaregiverType CaregiverType,
    string? ObservedCondition,
    string? Activities,
    int? Systolic,
    int? Diastolic,
    int? Pulse,
    decimal? Temperature,
    string? Notes,
    VisitReportAssessment Assessment,
    DateTime? StartedOnUtc,
    DateTime? CompletedOnUtc,
    DateTime? MeasurementTakenOnUtc,
    DateTime SubmittedOnUtc,
    bool PhotoAvailable);

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

/// <summary>
/// Caregiver-only booking detail (unchanged access boundary: the booking must
/// belong to the calling caregiver). F6 adds the persisted cancellation facts to
/// the response for this read only: the recorded actor side, action, reason
/// category, refund entitlement and refund decision reason, plus a refund state
/// resolved with the fact. No status ledger, event history or derived timeline
/// is invented — every added value is read from the stored row.
/// <para>
/// The caregiver record additions are read from the real stored rows too:
/// <c>visitHistory</c> from the visit and medical reports of this booking,
/// <c>familyReview</c> from the booking review, and <c>upcomingVisits</c> is
/// always <c>null</c> — the meaning of "upcoming" is undecided (the mobile
/// team's MOB-09 question), so no other or future booking is mapped as a visit
/// and no synthetic schedule is produced.
/// </para>
/// </summary>
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

        BookingDetailResponse response = BookingDetailMapper.ToResponse(booking, elderly);

        BookingCancellationFact? fact = await _dbContext.BookingCancellationFacts
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.BookingId == request.BookingId, cancellationToken);

        if (fact is not null)
        {
            response = response with
            {
                RefundState = BookingRefundStates.ResolveWithFact(booking, fact),
                CancellationActorSide = fact.ActorSide,
                CancellationAction = fact.Action,
                CancellationReasonCategory = fact.ReasonCategory?.ToString(),
                RefundEntitlement = fact.RefundEntitlement,
                RefundDecisionReason = fact.RefundDecisionReason
            };
        }

        // The real stored records of this booking. Every one of them is read
        // for this single booking; nothing is mapped from another booking.
        List<VisitReport> visitReports = await _dbContext.VisitReports
            .AsNoTracking()
            .Where(report => report.BookingId == request.BookingId)
            .ToListAsync(cancellationToken);

        List<MedicalReport> medicalReports = await _dbContext.MedicalReports
            .AsNoTracking()
            .Where(report => report.BookingId == request.BookingId)
            .ToListAsync(cancellationToken);

        BookingReview? review = await _dbContext.BookingReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.BookingId == request.BookingId,
                cancellationToken);

        response = response with
        {
            VisitHistory = BuildVisitHistory(visitReports, medicalReports),
            // Always null: the mobile team's MOB-09 answer decides whether
            // "upcoming" means this booking or the caregiver's other bookings.
            // Until then no other or future booking is mapped as a visit.
            UpcomingVisits = null,
            FamilyReview = review is null
                ? null
                : new BookingReviewResponse(
                    review.Id.Value,
                    review.BookingId.Value,
                    review.Rating,
                    review.Comment,
                    review.IsAnonymous,
                    review.CreatedOnUtc)
        };

        return Result<BookingDetailResponse>.Success(response);
    }

    /// <summary>
    /// The visit and medical reports of this booking, newest first. The list
    /// is <c>null</c> — not an empty array — when the booking has no report
    /// yet, so "nothing was recorded" is never confused with "something was
    /// recorded but empty".
    /// </summary>
    private static IReadOnlyList<CaregiverBookingVisitHistoryItem>? BuildVisitHistory(
        List<VisitReport> visitReports,
        List<MedicalReport> medicalReports)
    {
        if (visitReports.Count == 0 && medicalReports.Count == 0)
        {
            return null;
        }

        IEnumerable<CaregiverBookingVisitHistoryItem> items = visitReports
            .Select(report => new CaregiverBookingVisitHistoryItem(
                report.Id.Value,
                "visit",
                report.CaregiverType,
                report.ObservedCondition,
                report.Activities,
                null,
                null,
                null,
                null,
                report.Notes,
                report.Assessment,
                report.StartedOnUtc,
                report.CompletedOnUtc,
                null,
                report.SubmittedOnUtc,
                false));

        items = items.Concat(medicalReports
            .Select(report => new CaregiverBookingVisitHistoryItem(
                report.Id.Value,
                "medical",
                report.CaregiverType,
                null,
                null,
                report.Systolic,
                report.Diastolic,
                report.Pulse,
                report.Temperature,
                report.Notes,
                report.Assessment,
                null,
                null,
                report.MeasurementTakenOnUtc,
                report.SubmittedOnUtc,
                report.PhotoKey is not null)));

        return items
            .OrderByDescending(item => item.SubmittedOnUtc)
            .ThenByDescending(item => item.ReportId)
            .ToList();
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
                elderly.HealthNotes,
                // The emergency contact stays null for a caregiver: no
                // emergency-contact access or status model exists, so none is
                // invented and no caregiver access is added.
                null,
                !string.IsNullOrWhiteSpace(elderly.ProfileImageKey))
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
