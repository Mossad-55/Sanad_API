using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record SubscriptionBenefitResponse(SubscriptionBenefitKey Key, bool IsIncluded);

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
    int? PaymobSubscriptionPlanId = null);

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
            plan.Benefits.Select(benefit => new SubscriptionBenefitResponse(benefit.Key, benefit.IsIncluded)).ToList(),
            plan.PaymobSubscriptionPlanId);
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
