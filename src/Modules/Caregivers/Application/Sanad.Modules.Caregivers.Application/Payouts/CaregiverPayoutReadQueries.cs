using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Caregivers.Application.Payouts;

/// <summary>
/// Caregiver-facing payout read models (contract: plan.md §4). Statuses keep
/// the merged T4 labels: <c>Paid</c> is the transferred payout (the historic
/// "Transferred" label is an alias of Paid and is documented, not renamed);
/// there is no Processing state anywhere on these surfaces.
/// </summary>
public sealed record CaregiverPayoutSummaryResponse(
    string? Currency,
    decimal UnpaidAmount,
    decimal TransferredAmount,
    decimal FailedAmount,
    DateTime? NextExpectedPayoutOnUtc);

public sealed record CaregiverPayoutListItemResponse(
    Guid Id,
    decimal Amount,
    string Currency,
    string Status,
    string? BankNameArabic,
    string? BankNameEnglish,
    string MaskedIban,
    string TransferReference,
    DateTime CreatedOnUtc,
    DateTime? TransferredOnUtc,
    string? FailureReason);

public sealed record CaregiverPayoutBookingItemResponse(
    Guid BookingId,
    decimal CaregiverAmount,
    string Currency);

public sealed record CaregiverPayoutDetailResponse(
    Guid Id,
    decimal Amount,
    string Currency,
    string Status,
    string? BankNameArabic,
    string? BankNameEnglish,
    string MaskedIban,
    string TransferReference,
    DateTime CreatedOnUtc,
    DateTime? TransferredOnUtc,
    string? FailureReason,
    IReadOnlyList<CaregiverPayoutBookingItemResponse> BookingItems);

/// <summary>
/// Honest-null payment policy read: only the admin-authored bilingual
/// descriptions are real fields; no minimum-payout amount or expected
/// processing time exists in the product, so they are always null.
/// </summary>
public sealed record CaregiverPaymentPolicyResponse(
    string? DescriptionArabic,
    string? DescriptionEnglish,
    bool RequiresVerifiedBankAccount,
    decimal? MinimumPayoutAmount,
    int? ExpectedProcessingDays);

public sealed record GetCaregiverPayoutSummaryQuery(
    Guid CaregiverId) : IQuery<CaregiverPayoutSummaryResponse>;

public sealed record GetCaregiverPayoutListQuery(
    Guid CaregiverId,
    PayoutStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<CaregiverPayoutListItemResponse>>;

public sealed record GetCaregiverPayoutDetailQuery(
    Guid CaregiverId,
    Guid PayoutId) : IQuery<CaregiverPayoutDetailResponse>;

public sealed record GetCaregiverPaymentPolicyQuery
    : IQuery<CaregiverPaymentPolicyResponse>;

public sealed class GetCaregiverPayoutSummaryQueryHandler(
    ICaregiversDbContext caregiversDb,
    IFamiliesDbContext familiesDb,
    ICaregiverPayoutPolicyReader policyReader,
    IDateTimeProvider clock) : IQueryHandler<
    GetCaregiverPayoutSummaryQuery,
    CaregiverPayoutSummaryResponse>
{
    public async Task<Result<CaregiverPayoutSummaryResponse>> Handle(
        GetCaregiverPayoutSummaryQuery request,
        CancellationToken cancellationToken)
    {
        CaregiverPayoutPolicyRates? rates =
            await policyReader.GetEffectiveAsync(
                clock.UtcNow,
                cancellationToken);

        if (rates is null)
        {
            // Fail closed: eligibility cannot be computed without an
            // effective payout delay, mirroring admin payout recording.
            return Result<CaregiverPayoutSummaryResponse>.Failure(
                CaregiverPayoutPolicyErrors.Missing);
        }

        var caregiverId = new CaregiverId(request.CaregiverId);

        List<CaregiverPayout> payouts =
            await caregiversDb.Payouts
                .AsNoTracking()
                .Where(p => p.CaregiverId == caregiverId)
                .ToListAsync(cancellationToken);

        List<Booking> bookings =
            await familiesDb.Bookings
                .AsNoTracking()
                .Where(
                    b => b.CaregiverId == caregiverId &&
                         b.Status == BookingStatus.Completed &&
                         b.CompletedOnUtc != null &&
                         b.PaidOnUtc != null)
                .ToListAsync(cancellationToken);

        decimal transferredAmount = payouts
            .Where(p => p.Status == PayoutStatus.Paid)
            .Sum(p => p.NetAmount);

        decimal failedAmount = payouts
            .Where(p => p.Status == PayoutStatus.Failed)
            .Sum(p => p.NetAmount);

        HashSet<Guid> paidBookingIds = payouts
            .Where(p => p.Status == PayoutStatus.Paid)
            .Select(p => p.BookingId.Value)
            .ToHashSet();

        // Unpaid and now eligible: completed + paid bookings past the
        // policy delay with no Paid payout row. A Failed payout row does
        // not clear the debt (a new row can be recorded), so those
        // bookings still count as unpaid.
        List<Booking> unpaidBookings =
            bookings
                .Where(
                    b => !paidBookingIds.Contains(b.Id.Value) &&
                         PayoutEligibility.Evaluate(
                             b.CompletedOnUtc,
                             b.PaidOnUtc,
                             rates.PayoutDelayHours,
                             clock.UtcNow).IsEligible)
                .ToList();

        decimal unpaidAmount = unpaidBookings.Sum(
            b => b.PriceSnapshot.BaseCaregiverFee);

        string? currency =
            payouts
                .OrderByDescending(p => p.RecordedOnUtc)
                .Select(p => p.Currency)
                .FirstOrDefault();

        currency ??= bookings
            .OrderByDescending(b => b.CompletedOnUtc)
            .Select(b => b.PriceSnapshot.Currency)
            .FirstOrDefault();

        DateTime? nextExpectedPayoutOnUtc = null;

        bool hasVerifiedAccount =
            await caregiversDb.PayoutAccounts
                .AsNoTracking()
                .AnyAsync(
                    a => a.CaregiverId == caregiverId &&
                         a.Status == PayoutAccountStatus.Verified,
                    cancellationToken);

        if (hasVerifiedAccount)
        {
            foreach (Booking booking in bookings
                         .Where(b => !paidBookingIds.Contains(b.Id.Value)))
            {
                var eligibility = PayoutEligibility.Evaluate(
                    booking.CompletedOnUtc,
                    booking.PaidOnUtc,
                    rates.PayoutDelayHours,
                    clock.UtcNow);

                if (eligibility.EligibleAfterUtc is not null &&
                    (nextExpectedPayoutOnUtc is null ||
                     eligibility.EligibleAfterUtc < nextExpectedPayoutOnUtc))
                {
                    nextExpectedPayoutOnUtc = eligibility.EligibleAfterUtc;
                }
            }
        }

        return Result<CaregiverPayoutSummaryResponse>.Success(
            new CaregiverPayoutSummaryResponse(
                currency,
                unpaidAmount,
                transferredAmount,
                failedAmount,
                nextExpectedPayoutOnUtc));
    }
}

public sealed class GetCaregiverPayoutListQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverPayoutListQuery,
    PagedResult<CaregiverPayoutListItemResponse>>
{
    public async Task<Result<PagedResult<CaregiverPayoutListItemResponse>>> Handle(
        GetCaregiverPayoutListQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        var caregiverId = new CaregiverId(request.CaregiverId);

        IQueryable<CaregiverPayout> query =
            dbContext.Payouts
                .AsNoTracking()
                .Where(p => p.CaregiverId == caregiverId);

        if (request.Status.HasValue)
        {
            query = query.Where(p => p.Status == request.Status.Value);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<CaregiverPayout> items =
            await query
                .OrderByDescending(p => p.RecordedOnUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        IReadOnlyDictionary<string, CaregiverPayoutBankName> bankNames =
            await LoadBankNamesAsync(
                dbContext,
                items.Select(p => p.BankCode),
                cancellationToken);

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<PagedResult<CaregiverPayoutListItemResponse>>.Success(
            new PagedResult<CaregiverPayoutListItemResponse>(
                items
                    .Select(p => ToListItem(p, bankNames))
                    .ToList(),
                page,
                pageSize,
                totalCount,
                totalPages));
    }

    internal static CaregiverPayoutListItemResponse ToListItem(
        CaregiverPayout payout,
        IReadOnlyDictionary<string, CaregiverPayoutBankName> bankNames)
    {
        bankNames.TryGetValue(payout.BankCode, out CaregiverPayoutBankName? bank);

        return new CaregiverPayoutListItemResponse(
            payout.Id.Value,
            payout.GrossAmount,
            payout.Currency,
            payout.Status.ToString(),
            bank?.ArabicName,
            bank?.EnglishName,
            payout.MaskedIban,
            payout.TransferReference,
            payout.RecordedOnUtc,
            payout.Status == PayoutStatus.Paid ? payout.RecordedOnUtc : null,
            payout.FailureReason);
    }

    internal static async Task<
        IReadOnlyDictionary<string, CaregiverPayoutBankName>> LoadBankNamesAsync(
        ICaregiversDbContext dbContext,
        IEnumerable<string> bankCodes,
        CancellationToken cancellationToken)
    {
        List<string> codes = bankCodes.Distinct().ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, CaregiverPayoutBankName>(
                StringComparer.Ordinal);
        }

        return await dbContext.Banks
            .AsNoTracking()
            .Where(b => codes.Contains(b.Code))
            .ToDictionaryAsync(
                b => b.Code,
                b => new CaregiverPayoutBankName(
                    b.ArabicName,
                    b.EnglishName),
                StringComparer.Ordinal,
                cancellationToken);
    }
}

internal sealed record CaregiverPayoutBankName(
    string ArabicName,
    string EnglishName);

public sealed class GetCaregiverPayoutDetailQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverPayoutDetailQuery,
    CaregiverPayoutDetailResponse>
{
    public async Task<Result<CaregiverPayoutDetailResponse>> Handle(
        GetCaregiverPayoutDetailQuery request,
        CancellationToken cancellationToken)
    {
        var caregiverId = new CaregiverId(request.CaregiverId);

        CaregiverPayout? payout =
            await dbContext.Payouts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    p => p.Id == new CaregiverPayoutId(request.PayoutId) &&
                         p.CaregiverId == caregiverId,
                    cancellationToken);

        if (payout is null)
        {
            return Result<CaregiverPayoutDetailResponse>.Failure(
                CaregiverPayoutErrors.NotFound);
        }

        IReadOnlyDictionary<string, CaregiverPayoutBankName> bankNames =
            await GetCaregiverPayoutListQueryHandler.LoadBankNamesAsync(
                dbContext,
                [payout.BankCode],
                cancellationToken);

        CaregiverPayoutListItemResponse item =
            GetCaregiverPayoutListQueryHandler.ToListItem(
                payout,
                bankNames);

        return Result<CaregiverPayoutDetailResponse>.Success(
            new CaregiverPayoutDetailResponse(
                item.Id,
                item.Amount,
                item.Currency,
                item.Status,
                item.BankNameArabic,
                item.BankNameEnglish,
                item.MaskedIban,
                item.TransferReference,
                item.CreatedOnUtc,
                item.TransferredOnUtc,
                item.FailureReason,
                // Payouts are per-booking rows; the one-element array
                // matches the coded mobile shape and stays forward
                // compatible with future grouped payouts.
                [new CaregiverPayoutBookingItemResponse(
                    payout.BookingId.Value,
                    payout.GrossAmount,
                    payout.Currency)]));
    }
}

public sealed class GetCaregiverPaymentPolicyQueryHandler(
    ICaregiverPayoutPolicyReader policyReader,
    IDateTimeProvider clock) : IQueryHandler<
    GetCaregiverPaymentPolicyQuery,
    CaregiverPaymentPolicyResponse>
{
    public async Task<Result<CaregiverPaymentPolicyResponse>> Handle(
        GetCaregiverPaymentPolicyQuery request,
        CancellationToken cancellationToken)
    {
        CaregiverPayoutPolicyRates? rates =
            await policyReader.GetEffectiveAsync(
                clock.UtcNow,
                cancellationToken);

        return Result<CaregiverPaymentPolicyResponse>.Success(
            new CaregiverPaymentPolicyResponse(
                rates?.DescriptionArabic,
                rates?.DescriptionEnglish,
                RequiresVerifiedBankAccount: true,
                MinimumPayoutAmount: null,
                ExpectedProcessingDays: null));
    }
}

/// <summary>
/// Admin selector read model for a currently eligible manual payout: only the
/// identifiers, the existing safe caregiver display names, the timestamps the
/// operator needs to justify the transfer, and the existing caregiver fee
/// entitlement. No payout-account, bank or IBAN field is exposed.
/// </summary>
public sealed record EligibleCaregiverPayoutBookingResponse(
    Guid BookingId,
    Guid CaregiverId,
    string ArabicFullName,
    string EnglishFullName,
    DateTime CompletedOnUtc,
    DateTime EligibleOnUtc,
    decimal CaregiverAmount,
    string Currency);

/// <summary>
/// Paged selector input. <see cref="CaregiverId"/> is the only optional filter,
/// matching the adjacent admin payout list.
/// </summary>
public sealed record GetEligibleCaregiverPayoutBookingsQuery(
    Guid? CaregiverId = null,
    int Page = 1,
    int PageSize = 20) : IQuery<
    PagedResult<EligibleCaregiverPayoutBookingResponse>>;

/// <summary>
/// Read-only eligible-booking selector for recording a manual caregiver payout.
/// Inclusion reuses the record command rules exactly: completed and paid with
/// both timestamps, elapsed effective payout delay, a verified payout account,
/// and no <c>Paid</c> payout row. A <c>Failed</c> payout row does not exclude the
/// booking, so it stays available for a later retry. A missing effective policy
/// fails closed with the existing payout-policy error. Nothing is written and no
/// provider is called.
/// </summary>
public sealed class GetEligibleCaregiverPayoutBookingsQueryHandler(
    ICaregiversDbContext caregiversDb,
    IFamiliesDbContext familiesDb,
    ICaregiverPayoutPolicyReader policyReader,
    IDateTimeProvider clock) : IQueryHandler<
    GetEligibleCaregiverPayoutBookingsQuery,
    PagedResult<EligibleCaregiverPayoutBookingResponse>>
{
    public async Task<Result<PagedResult<EligibleCaregiverPayoutBookingResponse>>> Handle(
        GetEligibleCaregiverPayoutBookingsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        CaregiverPayoutPolicyRates? rates =
            await policyReader.GetEffectiveAsync(
                clock.UtcNow,
                cancellationToken);

        if (rates is null)
        {
            // Fail closed exactly like admin payout recording: eligibility is
            // undefined without the configured payout delay.
            return Result<PagedResult<EligibleCaregiverPayoutBookingResponse>>
                .Failure(CaregiverPayoutPolicyErrors.Missing);
        }

        IQueryable<Booking> bookingQuery =
            familiesDb.Bookings
                .AsNoTracking()
                .Where(
                    b => b.Status == BookingStatus.Completed &&
                         b.CompletedOnUtc != null &&
                         b.PaidOnUtc != null);

        if (request.CaregiverId.HasValue)
        {
            var caregiverId = new CaregiverId(request.CaregiverId.Value);
            bookingQuery = bookingQuery.Where(b => b.CaregiverId == caregiverId);
        }

        List<Booking> bookings =
            await bookingQuery.ToListAsync(cancellationToken);

        HashSet<Guid> verifiedCaregiverIds =
            (await caregiversDb.PayoutAccounts
                .AsNoTracking()
                .Where(a => a.Status == PayoutAccountStatus.Verified)
                .ToListAsync(cancellationToken))
            .Select(a => a.CaregiverId.Value)
            .ToHashSet();

        HashSet<Guid> paidBookingIds =
            (await caregiversDb.Payouts
                .AsNoTracking()
                .Where(p => p.Status == PayoutStatus.Paid)
                .ToListAsync(cancellationToken))
            .Select(p => p.BookingId.Value)
            .ToHashSet();

        List<(Booking Booking, DateTime EligibleOnUtc)> eligible = [];
        foreach (Booking booking in bookings)
        {
            if (!verifiedCaregiverIds.Contains(booking.CaregiverId.Value) ||
                paidBookingIds.Contains(booking.Id.Value))
            {
                continue;
            }

            PayoutEligibility.Result eligibility = PayoutEligibility.Evaluate(
                booking.CompletedOnUtc,
                booking.PaidOnUtc,
                rates.PayoutDelayHours,
                clock.UtcNow);

            if (eligibility.IsEligible &&
                eligibility.EligibleAfterUtc is not null)
            {
                eligible.Add((booking, eligibility.EligibleAfterUtc.Value));
            }
        }

        // Oldest eligible first, matching the existing admin review queue, with a
        // stable booking-id tie-break.
        eligible.Sort(
            static (left, right) =>
            {
                int byEligibleTime = left.EligibleOnUtc.CompareTo(right.EligibleOnUtc);
                return byEligibleTime != 0
                    ? byEligibleTime
                    : left.Booking.Id.Value.CompareTo(right.Booking.Id.Value);
            });

        int totalCount = eligible.Count;
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        List<(Booking Booking, DateTime EligibleOnUtc)> pageItems =
            eligible
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

        var namesByCaregiverId =
            new Dictionary<Guid, (string Arabic, string English)>();

        List<EligibleCaregiverPayoutBookingResponse> items =
            new(pageItems.Count);

        foreach ((Booking booking, DateTime eligibleOnUtc) in pageItems)
        {
            Guid caregiverId = booking.CaregiverId.Value;

            if (!namesByCaregiverId.TryGetValue(caregiverId, out var names))
            {
                names = await LoadCaregiverDisplayNamesAsync(
                    caregiversDb,
                    booking.CaregiverId,
                    cancellationToken);
                namesByCaregiverId[caregiverId] = names;
            }

            items.Add(
                new EligibleCaregiverPayoutBookingResponse(
                    booking.Id.Value,
                    caregiverId,
                    names.Arabic,
                    names.English,
                    booking.CompletedOnUtc!.Value,
                    eligibleOnUtc,
                    booking.PriceSnapshot.BaseCaregiverFee,
                    booking.PriceSnapshot.Currency));
        }

        return Result<PagedResult<EligibleCaregiverPayoutBookingResponse>>.Success(
            new PagedResult<EligibleCaregiverPayoutBookingResponse>(
                items,
                page,
                pageSize,
                totalCount,
                totalPages));
    }

    private static async Task<(string Arabic, string English)>
        LoadCaregiverDisplayNamesAsync(
            ICaregiversDbContext caregiversDb,
            CaregiverId caregiverId,
            CancellationToken cancellationToken)
    {
        Caregiver? caregiver = await caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == caregiverId, cancellationToken);

        if (caregiver is null)
        {
            return (string.Empty, string.Empty);
        }

        CaregiverUserHeader? header = await caregiversDb.GetCaregiverUserHeaderAsync(
            caregiver.UserId,
            cancellationToken);

        return header is null
            ? (string.Empty, string.Empty)
            : (header.ArabicFullName, header.EnglishFullName);
    }
}
