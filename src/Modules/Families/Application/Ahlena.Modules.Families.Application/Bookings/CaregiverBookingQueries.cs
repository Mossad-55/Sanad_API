using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Elderlies;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Reports;

namespace Ahlena.Modules.Families.Application.Bookings;

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
    internal static IReadOnlyList<CaregiverBookingVisitHistoryItem>? BuildVisitHistory(
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

/// <summary>
/// Authorized caregiver read of the booking dependent's stored emergency
/// contact (mobile item M5). Access is deliberately narrow: the booking must
/// belong to the calling caregiver (else <c>Bookings.NotFound</c>, 404) and the
/// visit must be live — <c>InProgress</c>, or <c>Confirmed</c> with the visit
/// not yet checked in (no <c>StartedOnUtc</c>) — so the contact is available
/// only across the visit window and never for a past, cancelled or merely
/// pending booking. Otherwise the coded <c>Bookings.EmergencyContactNotAllowed</c>
/// denial (403) is returned. The response reuses the family
/// <see cref="EmergencyContactResponse"/> shape (<c>name</c>,
/// <c>relationship</c>, <c>phoneNumber</c>) and is <c>null</c> when the
/// dependent row stores none.
/// </summary>
public sealed record GetCaregiverBookingEmergencyContactQuery(
    CaregiverId CaregiverId,
    BookingId BookingId) : IQuery<EmergencyContactResponse?>;

/// <summary>
/// Shared not-found error for the caregiver booking record reads: an unknown
/// booking and a booking that belongs to another caregiver are deliberately
/// indistinguishable.
/// </summary>
public static class CaregiverBookingErrors
{
    public static readonly Error NotFound =
        new("Bookings.NotFound", "Booking not found for this caregiver.");
}

public static class CaregiverBookingEmergencyContactErrors
{
    public static readonly Error NotAllowed =
        new(
            "Bookings.EmergencyContactNotAllowed",
            "The emergency contact is available only while the visit is in progress or confirmed and not yet started.");
}

public sealed class GetCaregiverBookingEmergencyContactQueryHandler
    : IQueryHandler<GetCaregiverBookingEmergencyContactQuery, EmergencyContactResponse?>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCaregiverBookingEmergencyContactQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<EmergencyContactResponse?>> Handle(
        GetCaregiverBookingEmergencyContactQuery request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId,
                cancellationToken);

        if (booking is null)
        {
            return Result<EmergencyContactResponse?>.Failure(
                CaregiverBookingErrors.NotFound);
        }

        // The emergency contact is exposed only across the live visit window.
        // A Confirmed booking whose visit already started (StartedOnUtc is set)
        // is past its check-in and is not allowed; a Completed/Cancelled/Expired
        // booking never allows it either.
        bool allowed =
            booking.Status == BookingStatus.InProgress ||
            (booking.Status == BookingStatus.Confirmed && booking.StartedOnUtc is null);

        if (!allowed)
        {
            return Result<EmergencyContactResponse?>.Failure(
                CaregiverBookingEmergencyContactErrors.NotAllowed);
        }

        Elderly? elderly = await _dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == booking.ElderlyId, cancellationToken);

        // Read from the dependent's stored fields via the existing family map:
        // no new contact model is invented and a row without a contact stays null.
        return Result<EmergencyContactResponse?>.Success(
            elderly is null ? null : GetFamilyEmergencyContactQueryHandler.Map(elderly));
    }
}

/// <summary>
/// Authorized caregiver read of a booking's recorded visit/medical report
/// history (mobile item M5) as its own route, reusing the exact projection the
/// caregiver booking detail already builds. Ownership is unchanged: the
/// booking must belong to the calling caregiver, else <c>Bookings.NotFound</c>
/// (404). The list is <c>null</c> — not an empty array — when no report was
/// recorded yet.
/// </summary>
public sealed record GetCaregiverBookingVisitHistoryQuery(
    CaregiverId CaregiverId,
    BookingId BookingId) : IQuery<IReadOnlyList<CaregiverBookingVisitHistoryItem>?>;

public sealed class GetCaregiverBookingVisitHistoryQueryHandler
    : IQueryHandler<GetCaregiverBookingVisitHistoryQuery, IReadOnlyList<CaregiverBookingVisitHistoryItem>?>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCaregiverBookingVisitHistoryQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<CaregiverBookingVisitHistoryItem>?>> Handle(
        GetCaregiverBookingVisitHistoryQuery request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId,
                cancellationToken);

        if (booking is null)
        {
            return Result<IReadOnlyList<CaregiverBookingVisitHistoryItem>?>.Failure(
                CaregiverBookingErrors.NotFound);
        }

        List<VisitReport> visitReports = await _dbContext.VisitReports
            .AsNoTracking()
            .Where(report => report.BookingId == request.BookingId)
            .ToListAsync(cancellationToken);

        List<MedicalReport> medicalReports = await _dbContext.MedicalReports
            .AsNoTracking()
            .Where(report => report.BookingId == request.BookingId)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CaregiverBookingVisitHistoryItem>?>.Success(
            GetCaregiverBookingDetailQueryHandler.BuildVisitHistory(visitReports, medicalReports));
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
