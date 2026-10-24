using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
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
    string? CancellationReason,
    Guid? RefundAttemptClaimId = null,
    bool IsRefundOutcomeAmbiguous = false,
    bool IsRefundAttemptInFlight = false,
    string? FamilyName = null,
    string? CaregiverArabicFullName = null,
    string? CaregiverEnglishFullName = null);

public sealed record PagedAdminBookings(
    IReadOnlyList<AdminBookingListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ListAdminBookingsQuery(
    int Page,
    int PageSize,
    AdminBookingFinanceFilter Finance = AdminBookingFinanceFilter.All,
    BookingStatus? Status = null)
    : IQuery<PagedAdminBookings>;

public sealed class ListAdminBookingsQueryHandler
    : IQueryHandler<ListAdminBookingsQuery, PagedAdminBookings>
{
    /// <summary>
    /// The de-personalized family name the Families aggregate stores on deletion
    /// (Family.MarkDeleted). A deleted family must not surface this sentinel as a display
    /// name; it projects to null like any other missing identity.
    /// </summary>
    private const string DeletedFamilySentinelName = "Deleted family";

    /// <summary>
    /// Every status/finance filter problem carries the same coded 400: undefined filter values
    /// (MVC enum binding accepts undefined numbers, so the handler must validate) and
    /// status/finance combinations whose intersection is structurally empty. One code keeps
    /// client handling single-shaped.
    /// </summary>
    public const string InvalidStatusFilterErrorCode = "Bookings.InvalidStatusFilter";

    private readonly IFamiliesDbContext _dbContext;
    private readonly IMedicalCaregiverGateway _caregiverDisplayNames;

    public ListAdminBookingsQueryHandler(
        IFamiliesDbContext dbContext,
        IMedicalCaregiverGateway caregiverDisplayNames)
    {
        _dbContext = dbContext;
        _caregiverDisplayNames = caregiverDisplayNames;
    }

    /// <summary>
    /// Back-compat constructor for existing readers built before display-name enrichment existed
    /// (shared tests that assert the legacy projection). It wires a gateway that publishes no
    /// caregiver names, so items read exactly like caregivers absent from the directory. Production
    /// wiring binds the full constructor (the container selects the most resolvable one).
    /// </summary>
    public ListAdminBookingsQueryHandler(IFamiliesDbContext dbContext)
        : this(dbContext, NoPublishedNamesGateway.Instance)
    {
    }

    public async Task<Result<PagedAdminBookings>> Handle(
        ListAdminBookingsQuery request,
        CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;

        if (!Enum.IsDefined(request.Finance))
        {
            return Result<PagedAdminBookings>.Failure(new Error(
                InvalidStatusFilterErrorCode,
                "The supplied finance filter is not a defined finance filter."));
        }

        if (request.Status is { } requestedStatus && !Enum.IsDefined(requestedStatus))
        {
            return Result<PagedAdminBookings>.Failure(new Error(
                InvalidStatusFilterErrorCode,
                "The supplied status filter is not a defined booking status."));
        }

        IQueryable<Booking> query = _dbContext.Bookings.AsNoTracking();

        if (request.Status is { } status)
        {
            // An explicit status means "exactly this status". A non-default finance predicate may
            // only combine with statuses that already belong to that predicate, so a rejected
            // combination is exactly the one whose intersection is structurally empty — an empty
            // page is never silently replaced by closed rows.
            bool statusBelongsToFinance = request.Finance switch
            {
                AdminBookingFinanceFilter.All => true,
                AdminBookingFinanceFilter.Cancelled =>
                    BookingRefundStates.CancelledStatuses.Contains(status),
                AdminBookingFinanceFilter.FailedRefund =>
                    BookingRefundStates.ClosedWithoutRefundStatus.Contains(status),
                AdminBookingFinanceFilter.Refunded => status == BookingStatus.Refunded,
                _ => false,
            };

            if (!statusBelongsToFinance)
            {
                return Result<PagedAdminBookings>.Failure(new Error(
                    InvalidStatusFilterErrorCode,
                    "The supplied status is not a member of the supplied finance filter."));
            }

            query = query.Where(b => b.Status == status);

            if (request.Finance != AdminBookingFinanceFilter.All)
            {
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
                    // Unreachable: the finance filter was validated as defined above.
                    _ => throw new InvalidOperationException(
                        "Unreachable: the finance filter was validated."),
                };
            }
        }
        else
        {
            // Absent status: the documented closed-list default, unchanged.
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
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<Booking> bookings = await query
            .OrderByDescending(b => b.CancelledOnUtc ?? b.RefundedOnUtc ?? b.UpdatedOnUtc)
            // The unique tiebreaker keeps page membership deterministic when an ordering key
            // (typically a shared cancellation timestamp) ties across rows. The ordering key must
            // stay translatable: unwrapping the id (`b.Id.Value`) does not translate for the
            // relational provider, so the entity id is ordered directly and BookingId implements
            // IComparable to keep CLR-side sorting of materialized rows working too.
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        Dictionary<BookingId, BookingCancellationFact> factsByBookingId = new();
        Dictionary<Guid, string> familyNamesByFamilyId = new();
        Dictionary<Guid, CaregiverDisplayName> caregiverNamesByCaregiverId = new();

        if (bookings.Count > 0)
        {
            List<BookingId> pageBookingIds = bookings.Select(b => b.Id).ToList();

            List<BookingCancellationFact> facts = await _dbContext.BookingCancellationFacts
                .AsNoTracking()
                .Where(f => pageBookingIds.Contains(f.BookingId))
                .ToListAsync(cancellationToken);

            List<Guid> pageFamilyIds = bookings.Select(b => b.FamilyId.Value).Distinct().ToList();
            List<Guid> pageCaregiverIds = bookings.Select(b => b.CaregiverId.Value).Distinct().ToList();

            // Batched per page: one fact read, one family read and one caregiver-directory read,
            // never per-item.
            factsByBookingId = facts.ToDictionary(f => f.BookingId);

            // The id stays in its mapped wrapper for both the filter and the projection: unwrapping
            // it (f.Id.Value) is not translatable for the relational provider, so it is unwrapped
            // after materialization instead.
            List<FamilyId> pageFamilyIdsTyped = pageFamilyIds.Select(id => new FamilyId(id)).ToList();
            familyNamesByFamilyId = (await _dbContext.Families
                    .AsNoTracking()
                    .Where(f => pageFamilyIdsTyped.Contains(f.Id))
                    .Select(f => new { f.Id, f.Name })
                    .ToListAsync(cancellationToken))
                .ToDictionary(family => family.Id.Value, family => family.Name);

            IReadOnlyDictionary<Guid, CaregiverDisplayName> caregiverNames =
                await _caregiverDisplayNames.GetCaregiverDisplayNamesAsync(
                    pageCaregiverIds,
                    cancellationToken);

            caregiverNamesByCaregiverId = caregiverNames.ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
        }

        var items = bookings.Select(b =>
        {
            BookingRefundState refundState = factsByBookingId.TryGetValue(b.Id, out BookingCancellationFact? fact)
                ? BookingRefundStates.ResolveWithFact(b, fact)
                : BookingRefundStates.Resolve(b);

            familyNamesByFamilyId.TryGetValue(b.FamilyId.Value, out string? familyName);
            caregiverNamesByCaregiverId.TryGetValue(b.CaregiverId.Value, out CaregiverDisplayName? caregiver);

            return new AdminBookingListItemResponse(
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
                refundState,
                b.PaidOnUtc,
                b.CancelledOnUtc,
                b.RefundedOnUtc,
                b.CancellationReason,
                b.RefundAttemptClaimId,
                b.IsRefundOutcomeAmbiguous,
                b.IsRefundAttemptInFlight,
                FamilyDisplayName(familyName),
                caregiver?.ArabicFullName,
                caregiver?.EnglishFullName);
        }).ToList();

        return Result<PagedAdminBookings>.Success(
            new PagedAdminBookings(items, page, pageSize, totalCount));
    }

    /// <summary>
    /// A deleted family's de-personalized sentinel never renders as a display name; it reads
    /// exactly like a missing identity.
    /// </summary>
    private static string? FamilyDisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name == DeletedFamilySentinelName
            ? null
            : name;

    /// <summary>A gateway stub that publishes no caregiver display names at all.</summary>
    private sealed class NoPublishedNamesGateway : IMedicalCaregiverGateway
    {
        public static readonly NoPublishedNamesGateway Instance = new();

        public Task<bool> IsActiveMedicalCaregiverAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MedicalCaregiverRecipientPage(
                new List<MedicalCaregiverRecipient>(),
                0));
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

        var response = BookingDetailMapper.ToResponse(booking, elderly);

        BookingCancellationFact? fact = await _dbContext.BookingCancellationFacts
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.BookingId == request.BookingId, cancellationToken);

        if (fact is not null)
        {
            response = response with
            {
                RefundState = BookingRefundStates.ResolveWithFact(booking, fact)
            };
        }

        return Result<BookingDetailResponse>.Success(response);
    }
}
