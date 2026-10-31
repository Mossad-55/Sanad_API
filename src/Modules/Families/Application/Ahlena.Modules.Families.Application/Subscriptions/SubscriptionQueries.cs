using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;

namespace Ahlena.Modules.Families.Application.Subscriptions;

public sealed record SubscriptionBenefitResponse(
    SubscriptionBenefitKey Key,
    bool IsIncluded,
    string? ArabicLabel = null,
    string? EnglishLabel = null);

public sealed record SubscriptionLimitResponse(SubscriptionLimitKind Kind, int? Value);

public sealed record SubscriptionPlanResponse(
    Guid Id,
    string Key,
    int Version,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    SubscriptionLimitResponse MemberLimit,
    SubscriptionLimitResponse MonthlyBookingLimit,
    SubscriptionRollover Rollover,
    bool IsAvailableForNewSales,
    DateTime CreatedOnUtc,
    DateTime? PublishedOnUtc,
    IReadOnlyList<SubscriptionBenefitResponse> Benefits,
    int? PaymobSubscriptionPlanId = null,
    // Version-owned authored copy. Absent on versions whose author supplied
    // none (and on every version created before the copy existed).
    string? ArabicName = null,
    string? EnglishName = null,
    string? ArabicDescription = null,
    string? EnglishDescription = null);

public sealed record SubscriptionSnapshotResponse(
    Guid Id,
    string PlanKey,
    int PlanVersion,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    SubscriptionLimitResponse MemberLimit,
    SubscriptionLimitResponse MonthlyBookingLimit,
    SubscriptionRollover Rollover,
    bool AutoRenewEnabled,
    DateTime? CancellationRequestedOnUtc,
    DateTime CurrentPeriodEndsOnUtc,
    DateTime? RenewalGraceEndsOnUtc,
    DateTime? LastRenewalFailedOnUtc,
    DateTime CreatedOnUtc,
    IReadOnlyList<SubscriptionBenefitResponse> Benefits,
    PendingDowngradeResponse? PendingDowngrade = null);
public sealed record PendingDowngradeResponse(string PlanKey, int PlanVersion, decimal Price, SubscriptionCycle Cycle, string Currency);

public sealed record CurrentSubscriptionResponse(SubscriptionSnapshotResponse? CurrentSubscription);
public sealed record SubscriptionCouponResponse(
    Guid Id,
    string Code,
    Guid PlanVersionId,
    decimal DiscountPercentage,
    DateTime ExpiresOnUtc,
    DateTime CreatedOnUtc);
public sealed record GetSubscriptionCouponsQuery : IQuery<IReadOnlyList<SubscriptionCouponResponse>>;
public sealed record GetSubscriptionCouponQuery(Guid CouponId) : IQuery<SubscriptionCouponResponse>;

public sealed record GetSubscriptionCatalogQuery(UserId UserId) : IQuery<IReadOnlyList<SubscriptionPlanResponse>>;

public sealed record GetCurrentSubscriptionQuery(UserId UserId) : IQuery<CurrentSubscriptionResponse>;

public sealed class GetSubscriptionCatalogQueryHandler : IQueryHandler<GetSubscriptionCatalogQuery, IReadOnlyList<SubscriptionPlanResponse>>
{
    private readonly IFamiliesDbContext _db;

    public GetSubscriptionCatalogQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<SubscriptionPlanResponse>>> Handle(
        GetSubscriptionCatalogQuery request,
        CancellationToken cancellationToken)
    {
        if (!await IsOwnerAsync(request.UserId, cancellationToken))
            return FamilyErrors.NotOwner;

        var plans = await _db.SubscriptionPlanVersions
            .AsNoTracking()
            .Where(plan => plan.IsPublished)
            .OrderBy(plan => plan.Key)
            .ThenBy(plan => plan.Version)
            .ToListAsync(cancellationToken);

        return plans.Select(Map).ToList();
    }

    private async Task<bool> IsOwnerAsync(UserId userId, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, userId, cancellationToken);
        return family is not null && FamilyAccess.IsOwner(family, userId);
    }

    private static SubscriptionPlanResponse Map(SubscriptionPlanVersion plan) =>
        new(
            plan.Id,
            plan.Key,
            plan.Version,
            plan.Price,
            plan.Cycle,
            plan.Currency,
            new(plan.MemberLimitKind, plan.MemberLimitValue),
            new(plan.MonthlyBookingLimitKind, plan.MonthlyBookingLimitValue),
            plan.Rollover,
            plan.IsAvailableForNewSales,
            plan.CreatedOnUtc,
            plan.PublishedOnUtc,
            plan.Benefits.Select(benefit => new SubscriptionBenefitResponse(
                benefit.Key,
                benefit.IsIncluded,
                benefit.ArabicLabel,
                benefit.EnglishLabel)).ToList(),
            plan.PaymobSubscriptionPlanId,
            plan.ArabicName,
            plan.EnglishName,
            plan.ArabicDescription,
            plan.EnglishDescription);
}

public sealed class GetSubscriptionCouponsQueryHandler : IQueryHandler<GetSubscriptionCouponsQuery, IReadOnlyList<SubscriptionCouponResponse>>
{
    private readonly IFamiliesDbContext _db;
    public GetSubscriptionCouponsQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<SubscriptionCouponResponse>>> Handle(GetSubscriptionCouponsQuery request, CancellationToken cancellationToken)
    {
        var coupons = await _db.SubscriptionCoupons.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken);
        return coupons.Select(Map).ToList();
    }

    internal static SubscriptionCouponResponse Map(SubscriptionCoupon coupon) => new(
        coupon.Id, coupon.Code, coupon.PlanVersionId, coupon.DiscountPercentage,
        coupon.ExpiresOnUtc, coupon.CreatedOnUtc);
}

public sealed class GetSubscriptionCouponQueryHandler : IQueryHandler<GetSubscriptionCouponQuery, SubscriptionCouponResponse>
{
    private static readonly Error NotFound = new("Subscriptions.Coupon.NotFound", "Subscription coupon was not found.");
    private readonly IFamiliesDbContext _db;
    public GetSubscriptionCouponQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<SubscriptionCouponResponse>> Handle(GetSubscriptionCouponQuery request, CancellationToken cancellationToken)
    {
        var coupon = await _db.SubscriptionCoupons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CouponId, cancellationToken);
        return coupon is null ? Result<SubscriptionCouponResponse>.Failure(NotFound) : GetSubscriptionCouponsQueryHandler.Map(coupon);
    }
}

public sealed class GetCurrentSubscriptionQueryHandler : IQueryHandler<GetCurrentSubscriptionQuery, CurrentSubscriptionResponse>
{
    private readonly IFamiliesDbContext _db;

    public GetCurrentSubscriptionQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<CurrentSubscriptionResponse>> Handle(
        GetCurrentSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return FamilyErrors.NotOwner;

        var subscription = await _db.FamilySubscriptions
            .AsNoTracking()
            .Where(item => item.FamilyId == family.Id && item.IsCurrent)
            .SingleOrDefaultAsync(cancellationToken);

        return new CurrentSubscriptionResponse(subscription is null ? null : Map(subscription));
    }

    private static SubscriptionSnapshotResponse Map(FamilySubscription subscription) =>
        new(
            subscription.Id,
            subscription.PlanKey,
            subscription.PlanVersion,
            subscription.Price,
            subscription.Cycle,
            subscription.Currency,
            new(subscription.MemberLimitKind, subscription.MemberLimitValue),
            new(subscription.MonthlyBookingLimitKind, subscription.MonthlyBookingLimitValue),
            subscription.Rollover,
            subscription.AutoRenewEnabled,
            subscription.CancellationRequestedOnUtc,
            subscription.CurrentPeriodEndsOnUtc,
            subscription.RenewalGraceEndsOnUtc,
            subscription.LastRenewalFailedOnUtc,
            subscription.CreatedOnUtc,
            subscription.Benefits.Select(benefit => new SubscriptionBenefitResponse(benefit.Key, benefit.IsIncluded)).ToList(),
            subscription.PendingDowngrade is null ? null : new PendingDowngradeResponse(subscription.PendingDowngrade.PlanKey, subscription.PendingDowngrade.PlanVersion, subscription.PendingDowngrade.Price, subscription.PendingDowngrade.Cycle, subscription.PendingDowngrade.Currency));
}

/// <summary>
/// Stable errors for the owner's subscription payment history. The list and
/// detail surfaces are owner-only, mirroring the invoice routes; a foreign or
/// missing payment attempt is a 404 (never a 403), so the response cannot be
/// used to probe another family's attempts.
/// </summary>
public static class SubscriptionPaymentErrors
{
    public static readonly Error NotOwner =
        new("Subscriptions.Payment.NotOwner", "Only the family owner can view subscription payments.");

    public static readonly Error NotFound =
        new("Subscriptions.Payment.NotFound", "The subscription payment was not found.");

    public static readonly Error InvalidQuery =
        new("Subscriptions.Payment.InvalidQuery", "The subscription payment query is invalid.");
}

/// <summary>
/// One payment attempt as the family reads it. <see cref="Amount"/> is in EGP
/// <b>major</b> units (for example <c>340.86</c>), the same scale as the invoice
/// amounts — never the internal piastre integer used by refunds.
/// <para>
/// <see cref="PaidOnUtc"/> is the provider settlement instant only
/// (<c>SettledOnUtc</c>); a pending or failed attempt has no paid instant even
/// though it has a creation instant. <see cref="InvoiceId"/> is the stored
/// invoice linked to this attempt, or null when none was issued (for example a
/// plan-change attempt, which may have no invoice).
/// </para>
/// </summary>
public sealed record SubscriptionPaymentResponse(
    Guid PaymentAttemptId,
    decimal Amount,
    string Currency,
    SubscriptionPaymentAttemptStatus Status,
    DateTime? PaidOnUtc,
    Guid? InvoiceId,
    SubscriptionPaymentMethod Method,
    DateTime CreatedOnUtc,
    string PlanKey,
    int PlanVersion,
    bool IsRenewal,
    bool IsPlanChange);

public sealed record SubscriptionPaymentPageResponse(
    IReadOnlyList<SubscriptionPaymentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Owner-scoped payment history. <see cref="PaidFromUtc"/> and
/// <see cref="PaidToUtc"/> are inclusive, UTC-only bounds on the settlement
/// instant; supplying a local/unspecified instant or a reversed range is a
/// clean invalid-query 400 rather than a silent reinterpretation. Status and
/// method accept only defined enum values. The stable order is
/// <c>paidOnUtc DESC NULLS LAST, createdOnUtc DESC, paymentAttemptId DESC</c>.
/// </summary>
public sealed record GetFamilySubscriptionPaymentsQuery(
    UserId UserId,
    int Page = 1,
    int PageSize = 20,
    SubscriptionPaymentAttemptStatus? Status = null,
    SubscriptionPaymentMethod? Method = null,
    DateTime? PaidFromUtc = null,
    DateTime? PaidToUtc = null) : IQuery<SubscriptionPaymentPageResponse>;

public sealed class GetFamilySubscriptionPaymentsQueryHandler
    : IQueryHandler<GetFamilySubscriptionPaymentsQuery, SubscriptionPaymentPageResponse>
{
    private const int MaximumPageSize = 100;

    private readonly IFamiliesDbContext _db;

    public GetFamilySubscriptionPaymentsQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<SubscriptionPaymentPageResponse>> Handle(
        GetFamilySubscriptionPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > MaximumPageSize)
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.InvalidQuery);
        if (request.Status is { } status && !Enum.IsDefined(status))
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.InvalidQuery);
        if (request.Method is { } method && !Enum.IsDefined(method))
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.InvalidQuery);
        if (request.PaidFromUtc is { Kind: not DateTimeKind.Utc }
            || request.PaidToUtc is { Kind: not DateTimeKind.Utc })
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.InvalidQuery);
        if (request.PaidFromUtc is { } from && request.PaidToUtc is { } to && from > to)
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.InvalidQuery);

        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return Result<SubscriptionPaymentPageResponse>.Failure(SubscriptionPaymentErrors.NotOwner);

        IQueryable<SubscriptionPaymentAttempt> query = _db.SubscriptionPaymentAttempts
            .AsNoTracking()
            .Where(item => item.FamilyId == family.Id);

        if (request.Status is { } statusFilter)
            query = query.Where(item => item.Status == statusFilter);
        if (request.Method is { } methodFilter)
            query = query.Where(item => item.Method == methodFilter);
        if (request.PaidFromUtc is { } paidFrom)
        {
            query = query.Where(item => item.SettledOnUtc != null && item.SettledOnUtc >= paidFrom);
        }
        if (request.PaidToUtc is { } paidTo)
        {
            query = query.Where(item => item.SettledOnUtc != null && item.SettledOnUtc <= paidTo);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        int page = request.Page;
        int pageSize = request.PageSize;

        // NULLS LAST is expressed portably instead of with Npgsql's
        // `.NullsLast()`, which does not translate on the in-memory provider used
        // by focused tests. The first key groups paid attempts before unpaid ones;
        // the remaining keys make ties deterministic.
        var items = await query
            .OrderByDescending(item => item.SettledOnUtc == null ? 0 : 1)
            .ThenByDescending(item => item.SettledOnUtc)
            .ThenByDescending(item => item.CreatedOnUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new SubscriptionPaymentResponse(
                item.Id,
                item.TotalPayable,
                item.Currency,
                item.Status,
                item.SettledOnUtc,
                _db.SubscriptionInvoices
                    .Where(invoice => invoice.PaymentAttemptId == item.Id)
                    .Select(invoice => (Guid?)invoice.Id)
                    .FirstOrDefault(),
                item.Method,
                item.CreatedOnUtc,
                item.PlanKey,
                item.PlanVersion,
                item.IsRenewal,
                item.IsPlanChange))
            .ToListAsync(cancellationToken);

        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<SubscriptionPaymentPageResponse>.Success(
            new SubscriptionPaymentPageResponse(items, page, pageSize, totalCount, totalPages));
    }
}

/// <summary>
/// Owner-scoped detail for one payment attempt. A foreign or missing id is the
/// same <see cref="SubscriptionPaymentErrors.NotFound"/> so the detail route
/// does not reveal another family's attempt ids.
/// </summary>
public sealed record GetFamilySubscriptionPaymentQuery(UserId UserId, Guid PaymentAttemptId)
    : IQuery<SubscriptionPaymentResponse>;

public sealed class GetFamilySubscriptionPaymentQueryHandler
    : IQueryHandler<GetFamilySubscriptionPaymentQuery, SubscriptionPaymentResponse>
{
    private readonly IFamiliesDbContext _db;

    public GetFamilySubscriptionPaymentQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<SubscriptionPaymentResponse>> Handle(
        GetFamilySubscriptionPaymentQuery request,
        CancellationToken cancellationToken)
    {
        if (request.PaymentAttemptId == Guid.Empty)
            return Result<SubscriptionPaymentResponse>.Failure(SubscriptionPaymentErrors.NotFound);

        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return Result<SubscriptionPaymentResponse>.Failure(SubscriptionPaymentErrors.NotOwner);

        SubscriptionPaymentResponse? payment = await _db.SubscriptionPaymentAttempts
            .AsNoTracking()
            .Where(item => item.Id == request.PaymentAttemptId && item.FamilyId == family.Id)
            .Select(item => new SubscriptionPaymentResponse(
                item.Id,
                item.TotalPayable,
                item.Currency,
                item.Status,
                item.SettledOnUtc,
                _db.SubscriptionInvoices
                    .Where(invoice => invoice.PaymentAttemptId == item.Id)
                    .Select(invoice => (Guid?)invoice.Id)
                    .FirstOrDefault(),
                item.Method,
                item.CreatedOnUtc,
                item.PlanKey,
                item.PlanVersion,
                item.IsRenewal,
                item.IsPlanChange))
            .SingleOrDefaultAsync(cancellationToken);

        return payment is null
            ? Result<SubscriptionPaymentResponse>.Failure(SubscriptionPaymentErrors.NotFound)
            : Result<SubscriptionPaymentResponse>.Success(payment);
    }
}
