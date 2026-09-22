using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record AdminSubscriptionPlanResponse(
    Guid Id,
    string Key,
    int Version,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    SubscriptionLimitResponse MemberLimit,
    SubscriptionLimitResponse MonthlyBookingLimit,
    SubscriptionRollover Rollover,
    bool IsPublished,
    bool IsAvailableForNewSales,
    DateTime CreatedOnUtc,
    DateTime? PublishedOnUtc,
    IReadOnlyList<SubscriptionBenefitResponse> Benefits);

public sealed record PagedAdminSubscriptionPlans(
    IReadOnlyList<AdminSubscriptionPlanResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AdminFamilySubscriptionResponse(
    Guid Id,
    Guid FamilyId,
    string FamilyName,
    Guid OwnerUserId,
    string PlanKey,
    int PlanVersion,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    SubscriptionLimitResponse MemberLimit,
    SubscriptionLimitResponse MonthlyBookingLimit,
    SubscriptionRollover Rollover,
    bool IsCurrent,
    bool AutoRenewEnabled,
    DateTime? CancellationRequestedOnUtc,
    DateTime CurrentPeriodEndsOnUtc,
    DateTime CreatedOnUtc,
    IReadOnlyList<SubscriptionBenefitResponse> Benefits);

public sealed record PagedAdminFamilySubscriptions(
    IReadOnlyList<AdminFamilySubscriptionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminSubscriptionPlansQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedAdminSubscriptionPlans>;

public sealed record GetAdminSubscriptionPlanQuery(Guid PlanVersionId)
    : IQuery<AdminSubscriptionPlanResponse>;

public sealed record GetAdminFamilySubscriptionsQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedAdminFamilySubscriptions>;

public sealed record GetAdminFamilySubscriptionQuery(Guid SubscriptionId)
    : IQuery<AdminFamilySubscriptionResponse>;

public sealed class GetAdminSubscriptionPlansQueryHandler
    : IQueryHandler<GetAdminSubscriptionPlansQuery, PagedAdminSubscriptionPlans>
{
    private readonly IFamiliesDbContext _db;

    public GetAdminSubscriptionPlansQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<PagedAdminSubscriptionPlans>> Handle(
        GetAdminSubscriptionPlansQuery request,
        CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;
        IQueryable<SubscriptionPlanVersion> query = _db.SubscriptionPlanVersions.AsNoTracking();

        int totalCount = await query.CountAsync(cancellationToken);
        List<SubscriptionPlanVersion> plans = await query
            .OrderBy(plan => plan.Key)
            .ThenBy(plan => plan.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedAdminSubscriptionPlans(
            plans.Select(MapPlan).ToList(), page, pageSize, totalCount);
    }

    internal static AdminSubscriptionPlanResponse MapPlan(SubscriptionPlanVersion plan) => new(
        plan.Id,
        plan.Key,
        plan.Version,
        plan.Price,
        plan.Cycle,
        plan.Currency,
        new(plan.MemberLimitKind, plan.MemberLimitValue),
        new(plan.MonthlyBookingLimitKind, plan.MonthlyBookingLimitValue),
        plan.Rollover,
        plan.IsPublished,
        plan.IsAvailableForNewSales,
        plan.CreatedOnUtc,
        plan.PublishedOnUtc,
        plan.Benefits.Select(benefit => new SubscriptionBenefitResponse(benefit.Key, benefit.IsIncluded)).ToList());
}

public sealed class GetAdminSubscriptionPlanQueryHandler
    : IQueryHandler<GetAdminSubscriptionPlanQuery, AdminSubscriptionPlanResponse>
{
    private static readonly Error NotFound = new("Subscriptions.Plan.NotFound", "Subscription plan was not found.");
    private readonly IFamiliesDbContext _db;

    public GetAdminSubscriptionPlanQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<AdminSubscriptionPlanResponse>> Handle(
        GetAdminSubscriptionPlanQuery request,
        CancellationToken cancellationToken)
    {
        SubscriptionPlanVersion? plan = await _db.SubscriptionPlanVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.PlanVersionId, cancellationToken);

        return plan is null
            ? Result<AdminSubscriptionPlanResponse>.Failure(NotFound)
            : GetAdminSubscriptionPlansQueryHandler.MapPlan(plan);
    }
}

public sealed class GetAdminFamilySubscriptionsQueryHandler
    : IQueryHandler<GetAdminFamilySubscriptionsQuery, PagedAdminFamilySubscriptions>
{
    private readonly IFamiliesDbContext _db;

    public GetAdminFamilySubscriptionsQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<PagedAdminFamilySubscriptions>> Handle(
        GetAdminFamilySubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;
        IQueryable<FamilySubscription> query = _db.FamilySubscriptions.AsNoTracking();

        int totalCount = await query.CountAsync(cancellationToken);
        List<FamilySubscription> subscriptions = await query
            .OrderByDescending(subscription => subscription.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var familyIds = subscriptions.Select(subscription => subscription.FamilyId).Distinct().ToList();
        var families = await _db.Families
            .AsNoTracking()
            .Where(family => familyIds.Contains(family.Id))
            .ToDictionaryAsync(family => family.Id, cancellationToken);

        var items = subscriptions
            .Where(subscription => families.ContainsKey(subscription.FamilyId))
            .Select(subscription => MapSubscription(subscription, families[subscription.FamilyId]))
            .ToList();

        return new PagedAdminFamilySubscriptions(items, page, pageSize, totalCount);
    }

    internal static AdminFamilySubscriptionResponse MapSubscription(
        FamilySubscription subscription,
        Family family) => new(
        subscription.Id,
        subscription.FamilyId.Value,
        family.Name,
        family.OwnerUserId.Value,
        subscription.PlanKey,
        subscription.PlanVersion,
        subscription.Price,
        subscription.Cycle,
        subscription.Currency,
        new(subscription.MemberLimitKind, subscription.MemberLimitValue),
        new(subscription.MonthlyBookingLimitKind, subscription.MonthlyBookingLimitValue),
        subscription.Rollover,
        subscription.IsCurrent,
        subscription.AutoRenewEnabled,
        subscription.CancellationRequestedOnUtc,
        subscription.CurrentPeriodEndsOnUtc,
        subscription.CreatedOnUtc,
        subscription.Benefits.Select(benefit => new SubscriptionBenefitResponse(benefit.Key, benefit.IsIncluded)).ToList());
}

public sealed class GetAdminFamilySubscriptionQueryHandler
    : IQueryHandler<GetAdminFamilySubscriptionQuery, AdminFamilySubscriptionResponse>
{
    private static readonly Error NotFound = new("Subscriptions.Subscription.NotFound", "Family subscription was not found.");
    private readonly IFamiliesDbContext _db;

    public GetAdminFamilySubscriptionQueryHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<AdminFamilySubscriptionResponse>> Handle(
        GetAdminFamilySubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.FamilySubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.SubscriptionId, cancellationToken);

        if (subscription is null)
            return Result<AdminFamilySubscriptionResponse>.Failure(NotFound);

        var family = await _db.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == subscription.FamilyId, cancellationToken);

        return family is null
            ? Result<AdminFamilySubscriptionResponse>.Failure(NotFound)
            : GetAdminFamilySubscriptionsQueryHandler.MapSubscription(subscription, family);
    }
}
