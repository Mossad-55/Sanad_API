using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Discovery;

public sealed record CareHomeDiscoverySummary(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string ArabicDescription,
    string EnglishDescription,
    string Governorate,
    string City,
    string Area,
    decimal? StartingFromMonthlyPriceEgp);

public sealed record CareHomeDiscoveryPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CareHomeDiscoverySummary> Items);

public sealed record CareHomeDiscoveryRoomType(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? ArabicDescription,
    string? EnglishDescription,
    CareHomeAllocationMode AllocationMode,
    decimal MonthlyPriceEgp,
    IReadOnlyList<BilingualCareHomeItem> Amenities,
    string? CoverImageUrl);

public sealed record CareHomeDiscoveryDetail(
    CareHomeDiscoverySummary Summary,
    string Address,
    string ArabicAdmissionConditions,
    string EnglishAdmissionConditions,
    IReadOnlyList<BilingualCareHomeItem> Amenities,
    IReadOnlyList<BilingualCareHomeItem> MedicalServices,
    IReadOnlyList<CareHomeDiscoveryRoomType> RoomTypes,
    string? CoverImageUrl,
    IReadOnlyList<string> GalleryImageUrls,
    Guid? ContactUserId,
    IReadOnlyList<CareHomeBookingCancellationTierView>? CancellationTerms = null);

public sealed record GetCareHomeDiscoveryQuery(int Page, int PageSize) : IQuery<CareHomeDiscoveryPage>;

/// <summary>
/// Facility detail. <c>Requester</c> is the authenticated caller when one is
/// present (the route itself stays anonymous): the additive
/// <c>ContactUserId</c> (the facility owner's user id) is resolved only for
/// authenticated callers so they can open a booking-less direct chat with
/// the facility, and stays null for anonymous visitors.
/// </summary>
public sealed record GetCareHomeDiscoveryDetailQuery(Guid CareHomeId, UserId? Requester = null) : IQuery<CareHomeDiscoveryDetail>;

internal static class CareHomeDiscoveryErrors
{
    internal static readonly Error NotFound = new("CareHomes.Discovery.NotFound", "The care home was not found.");
    internal static readonly Error InvalidQuery = new("CareHomes.Discovery.InvalidQuery", "The discovery query is invalid.");
}

internal sealed class CareHomeDiscoveryReader(ICareHomesDbContext db, IDateTimeProvider clock)
{
    internal IQueryable<CareHomeFacility> EligibleQuery(DateOnly today)
    {
        IQueryable<CareHomeFacility> query = db.Facilities.AsNoTracking()
            .Where(x => x.Status == CareHomeStatus.Approved && x.ApprovedRevisionId != null)
            .Where(x => x.Revisions.Any(r => r.Id == x.ApprovedRevisionId && r.ApprovedOnUtc != null));
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            query = query.Where(facility => facility.Documents.Any(document =>
                document.ProfileRevisionId == facility.ApprovedRevisionId && document.Type == type &&
                document.Status == CareHomeDocumentStatus.Verified &&
                (document.VerifiedNonExpiring || (document.ExpiryDate != null && document.ExpiryDate >= today)) &&
                !facility.Documents.Any(newer =>
                    newer.ProfileRevisionId == facility.ApprovedRevisionId && newer.Type == type &&
                    (newer.CreatedOnUtc > document.CreatedOnUtc ||
                     (newer.CreatedOnUtc == document.CreatedOnUtc && newer.Id.CompareTo(document.Id) > 0)))));
        }
        return query;
    }

    internal async Task<List<CareHomePublicRecord>> ReadEligiblePage(int page, int pageSize, CancellationToken ct)
    {
        DateOnly today = CurrentCairoDate();
        var facilities = await EligibleQuery(today).OrderBy(x => x.Id)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Include(x => x.Revisions).Include(x => x.Documents).AsSplitQuery().ToListAsync(ct);
        return await MapEligible(facilities, today, ct);
    }

    internal Task<int> CountEligible(CancellationToken ct) => EligibleQuery(CurrentCairoDate()).CountAsync(ct);

    internal async Task<CareHomePublicRecord?> ReadEligibleDetail(Guid id, CancellationToken ct)
    {
        DateOnly today = CurrentCairoDate();
        CareHomeFacility? facility = await EligibleQuery(today).Where(x => x.Id == new CareHomeId(id))
            .Include(x => x.Revisions).Include(x => x.Documents).AsSplitQuery().SingleOrDefaultAsync(ct);
        return facility is null ? null : (await MapEligible([facility], today, ct)).Single();
    }

    private async Task<List<CareHomePublicRecord>> MapEligible(IReadOnlyList<CareHomeFacility> facilities, DateOnly today, CancellationToken ct)
    {
        CareHomeId[] facilityIds = facilities.Select(x => x.Id).ToArray();
        if (facilityIds.Length == 0)
            return [];

        // Build a bounded OR of scalar value-object equality comparisons. The
        // relational converter maps each CareHomeId constant to the UUID column,
        // avoiding collection/value-object-member translation.
        ParameterExpression parameter = Expression.Parameter(typeof(CareHomeRoomType), "roomType");
        Expression facility = Expression.Property(parameter, nameof(CareHomeRoomType.FacilityId));
        Expression? predicate = null;
        foreach (CareHomeId facilityId in facilityIds)
        {
            Expression comparison = Expression.Equal(facility, Expression.Constant(facilityId));
            predicate = predicate is null ? comparison : Expression.OrElse(predicate, comparison);
        }

        var roomFilter = Expression.Lambda<Func<CareHomeRoomType, bool>>(predicate!, parameter);
        var roomTypes = await db.RoomTypes.AsNoTracking()
            .Where(roomFilter)
            .Where(x => !x.IsArchived)
            .ToListAsync(ct);
        return facilities.Select(facility => TryCreate(facility, roomTypes.Where(x => x.FacilityId == facility.Id), today)!).ToList();
    }

    private DateOnly CurrentCairoDate() => CareHomePublicEligibility.CairoDate(clock.UtcNow);

    private static CareHomePublicRecord? TryCreate(CareHomeFacility facility, IEnumerable<CareHomeRoomType> roomTypes, DateOnly today)
    {
        if (!CareHomePublicEligibility.IsEligible(facility, today))
            return null;
        CareHomeProfileRevision revision = facility.Revisions.Single(x => x.Id == facility.ApprovedRevisionId && x.ApprovedOnUtc != null);

        var activeRoomTypes = roomTypes.Where(x => !x.IsArchived).ToArray();
        CareHomeDiscoverySummary summary = new(
            facility.Id.Value, revision.ArabicName, revision.EnglishName,
            revision.ArabicDescription, revision.EnglishDescription,
            revision.Governorate, revision.City, revision.Area,
            activeRoomTypes.Length == 0 ? null : activeRoomTypes.Min(x => x.MonthlyPriceEgp));
        return new CareHomePublicRecord(facility.Id.Value, summary, revision, activeRoomTypes);
    }

}

internal static class CareHomePublicEligibility
{
    private static readonly TimeZoneInfo CairoTimeZone = ResolveCairoTimeZone();

    internal static DateOnly CairoDate(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, CairoTimeZone));

    internal static bool IsEligible(CareHomeFacility facility, DateOnly today)
    {
        if (facility.Status != CareHomeStatus.Approved || facility.ApprovedRevisionId is not Guid approvedRevisionId ||
            !facility.Revisions.Any(revision => revision.Id == approvedRevisionId && revision.ApprovedOnUtc is not null))
            return false;

        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            CareHomeDocument? latest = facility.Documents
                .Where(document => document.ProfileRevisionId == approvedRevisionId && document.Type == type)
                .OrderByDescending(document => document.CreatedOnUtc)
                .ThenByDescending(document => document.Id)
                .FirstOrDefault();
            if (latest is null || !latest.IsUsableOn(today))
                return false;
        }
        return true;
    }

    private static TimeZoneInfo ResolveCairoTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
    }
}

internal sealed record CareHomePublicRecord(
    Guid Id,
    CareHomeDiscoverySummary Summary,
    CareHomeProfileRevision Revision,
    IReadOnlyList<CareHomeRoomType> ActiveRoomTypes);

public sealed class GetCareHomeDiscoveryQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetCareHomeDiscoveryQuery, CareHomeDiscoveryPage>
{
    public async Task<Result<CareHomeDiscoveryPage>> Handle(GetCareHomeDiscoveryQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 50 ||
            (long)(request.Page - 1) * request.PageSize > int.MaxValue)
            return Result<CareHomeDiscoveryPage>.Failure(CareHomeDiscoveryErrors.InvalidQuery);

        var reader = new CareHomeDiscoveryReader(db, clock);
        int total = await reader.CountEligible(ct);
        List<CareHomePublicRecord> records = await reader.ReadEligiblePage(request.Page, request.PageSize, ct);
        IReadOnlyList<CareHomeDiscoverySummary> items = records
            .Select(x => x.Summary)
            .ToArray();
        return Result<CareHomeDiscoveryPage>.Success(new(request.Page, request.PageSize, total, items));
    }
}

public sealed class GetCareHomeDiscoveryDetailQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetCareHomeDiscoveryDetailQuery, CareHomeDiscoveryDetail>
{
    public async Task<Result<CareHomeDiscoveryDetail>> Handle(GetCareHomeDiscoveryDetailQuery request, CancellationToken ct)
    {
        CareHomePublicRecord? record = await new CareHomeDiscoveryReader(db, clock)
            .ReadEligibleDetail(request.CareHomeId, ct);
        if (record is null)
            return Result<CareHomeDiscoveryDetail>.Failure(CareHomeDiscoveryErrors.NotFound);

        CareHomeProfileRevision revision = record.Revision;
        var approvedMedia = await db.ProfileMedia.AsNoTracking()
            .Where(x => x.ProfileRevisionId == revision.Id).OrderBy(x => x.Position).ToListAsync(ct);
        string? coverUrl = approvedMedia.FirstOrDefault(x => x.Kind == CareHomeProfileMediaKind.Cover) is { } cover
            ? $"/api/v1/care-homes/discovery/{record.Summary.Id:D}/media/{cover.Id:D}" : null;
        string[] galleryUrls = approvedMedia.Where(x => x.Kind == CareHomeProfileMediaKind.Gallery)
            .Select(x => $"/api/v1/care-homes/discovery/{record.Summary.Id:D}/media/{x.Id:D}").ToArray();

        // Additive contact user (Opus delta): only an authenticated caller
        // receives the owner's user id to open a booking-less direct chat;
        // anonymous visitors keep null.
        Guid? contactUserId = request.Requester is null
            ? null
            : await db.Facilities.AsNoTracking()
                .Where(x => x.Id == new CareHomeId(request.CareHomeId))
                .Select(x => (Guid?)x.OwnerUserId.Value)
                .SingleOrDefaultAsync(ct);

        // Additive room-type marketing content: type amenities plus the published
        // cover photo (public read, same privacy posture as facility media).
        var typeIds = record.ActiveRoomTypes.Select(x => x.Id).ToList();
        var typeMedia = await db.RoomTypeMedia.AsNoTracking()
            .Where(x => typeIds.Contains(x.RoomTypeId)).ToListAsync(ct);

        // Clause 6 / decisions #7, #9, #10: the family-facing page advertises the
        // facility's structured cancellation terms exactly when a booking made
        // right now would snapshot them. While the scheduled-start policy ships
        // dormant, legacy bookings keep the pre-policy schedule, so the page
        // hides the terms rather than promising a schedule no booking would
        // receive. Reading the same switch CareHomeRefundPolicySnapshotResolver
        // consults keeps display and snapshotting on one activation seam.
        IReadOnlyList<CareHomeBookingCancellationTierView>? cancellationTerms;
        try
        {
            cancellationTerms =
                CareHomeRefundPolicySnapshotResolver.ScheduledStartPolicyActivated
                    ? CareHomeCancellationTerms.Parse(revision.CancellationTermsJson)?.Tiers
                        .Select(tier => new CareHomeBookingCancellationTierView(
                            tier.RefundPercentage, tier.DeadlineHoursBeforeStart)).ToArray()
                    : null;
        }
        catch (DomainException)
        {
            // Once activated, the public page must not 500 on a profile
            // revision whose stored terms cannot be re-read: fail the read
            // closed with a stable code (out-of-band corruption only — the
            // owner save and admin review paths validate the terms).
            return Result<CareHomeDiscoveryDetail>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The facility's stored cancellation terms could not be read."));
        }

        return Result<CareHomeDiscoveryDetail>.Success(new(
            record.Summary,
            revision.Address,
            revision.ArabicAdmissionConditions,
            revision.EnglishAdmissionConditions,
            revision.Amenities,
            revision.MedicalServices,
            record.ActiveRoomTypes.Select(x => new CareHomeDiscoveryRoomType(
                x.Id, x.ArabicName, x.EnglishName, x.ArabicDescription, x.EnglishDescription,
                x.AllocationMode, x.MonthlyPriceEgp, x.Amenities,
                typeMedia.FirstOrDefault(m => m.RoomTypeId == x.Id && m.Kind == CareHomeRoomTypeMediaKind.Cover) is { } cover
                    ? $"/api/v1/care-homes/discovery/{record.Summary.Id:D}/room-types/{x.Id:D}/media/{cover.Id:D}/file" : null)).ToArray(), coverUrl, galleryUrls, contactUserId, cancellationTerms));
    }
}
