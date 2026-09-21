using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record CancelSubscriptionRenewalCommand(UserId UserId) : ICommand;
public sealed record ReenableSubscriptionAutoRenewCommand(UserId UserId) : ICommand;

public sealed record SubscriptionBenefitInput(SubscriptionBenefitKey Key, bool IsIncluded);
public sealed record SubscriptionLimitInput(SubscriptionLimitKind Kind, int? Value);
public sealed record CreateSubscriptionPlanVersionCommand(
    string Key,
    int Version,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    IReadOnlyCollection<SubscriptionBenefitInput> Benefits,
    SubscriptionLimitInput MemberLimit,
    SubscriptionLimitInput MonthlyBookingLimit,
    SubscriptionRollover Rollover,
    UserId ActorUserId) : ICommand<Guid>;

public sealed record PublishSubscriptionPlanVersionCommand(Guid PlanVersionId, UserId ActorUserId) : ICommand;

public sealed record CreateSubscriptionCouponCommand(
    string Code,
    Guid PlanVersionId,
    decimal DiscountPercentage,
    DateTime ExpiresOnUtc,
    UserId ActorUserId) : ICommand<Guid>;

public sealed record DeleteSubscriptionCouponCommand(Guid CouponId, UserId ActorUserId) : ICommand;

public sealed class CreateSubscriptionCouponCommandHandler
    : ICommandHandler<CreateSubscriptionCouponCommand, Guid>
{
    private static readonly Error Invalid = new("Subscriptions.Coupon.Invalid", "Coupon configuration is invalid.");
    private static readonly Error Duplicate = new("Subscriptions.Coupon.DuplicateCode", "A coupon with this code already exists.");
    private static readonly Error PlanNotFound = new("Subscriptions.Coupon.PlanNotFound", "The target subscription plan was not found.");
    private readonly IFamiliesDbContext _dbContext;

    public CreateSubscriptionCouponCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<Guid>> Handle(CreateSubscriptionCouponCommand request, CancellationToken cancellationToken)
    {
        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (await _dbContext.SubscriptionCoupons.AnyAsync(x => x.Code == code, cancellationToken))
            return Result<Guid>.Failure(Duplicate);

        var plan = await _dbContext.SubscriptionPlanVersions
            .SingleOrDefaultAsync(x => x.Id == request.PlanVersionId, cancellationToken);
        if (plan is null || !plan.IsPublished || !plan.IsAvailableForNewSales)
            return Result<Guid>.Failure(PlanNotFound);

        try
        {
            var coupon = SubscriptionCoupon.Create(
                code, request.PlanVersionId, request.DiscountPercentage, request.ExpiresOnUtc);
            _dbContext.SubscriptionCoupons.Add(coupon);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(coupon.Id);
        }
        catch (DbUpdateException exception) when (exception.ToString().Contains(
            "ux_subscription_coupons_code", StringComparison.Ordinal))
        {
            return Result<Guid>.Failure(Duplicate);
        }
        catch (DomainException exception)
        {
            return Result<Guid>.Failure(new Error("Subscriptions.Coupon.Invalid", exception.Message));
        }
    }
}

public sealed class DeleteSubscriptionCouponCommandHandler
    : ICommandHandler<DeleteSubscriptionCouponCommand>
{
    private static readonly Error NotFound = new("Subscriptions.Coupon.NotFound", "The coupon was not found.");
    private readonly IFamiliesDbContext _dbContext;

    public DeleteSubscriptionCouponCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result> Handle(DeleteSubscriptionCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await _dbContext.SubscriptionCoupons
            .SingleOrDefaultAsync(x => x.Id == request.CouponId, cancellationToken);
        if (coupon is null) return Result.Failure(NotFound);

        _dbContext.SubscriptionCoupons.Remove(coupon);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
public sealed class CreateSubscriptionPlanVersionCommandHandler
    : ICommandHandler<CreateSubscriptionPlanVersionCommand, Guid>
{
    private static readonly Error Duplicate = new("Subscriptions.Plan.DuplicateVersion", "A subscription plan with this key and version already exists.");
    private readonly IFamiliesDbContext _dbContext;
    private static readonly Error InvalidTerms = new("Subscriptions.Plan.InvalidTerms", "Subscription plan terms are invalid.");

    public CreateSubscriptionPlanVersionCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<Guid>> Handle(CreateSubscriptionPlanVersionCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key)
            || request.Benefits is null
            || request.MemberLimit is null
            || request.MonthlyBookingLimit is null)
            return Result<Guid>.Failure(InvalidTerms);

        if (await _dbContext.SubscriptionPlanVersions.AnyAsync(
                item => item.Key == request.Key.Trim() && item.Version == request.Version,
                cancellationToken))
            return Result<Guid>.Failure(Duplicate);

        try
        {
            var plan = SubscriptionPlan.Create(
                request.Key,
                request.Version,
                request.Price,
                request.Cycle,
                request.Currency,
                request.Benefits.Select(item => SubscriptionBenefit.Create(item.Key, item.IsIncluded)),
                CreateLimit(request.MemberLimit),
                CreateLimit(request.MonthlyBookingLimit),
                request.Rollover);
            var version = SubscriptionPlanVersion.Create(plan);
            _dbContext.SubscriptionPlanVersions.Add(version);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(version.Id);
        }
        catch (DbUpdateException exception) when (exception.ToString().Contains(
            "ux_subscription_plan_versions_key_version", StringComparison.Ordinal))
        {
            return Result<Guid>.Failure(Duplicate);
        }
        catch (DomainException exception)
        {
            return Result<Guid>.Failure(new Error("Subscriptions.Plan.InvalidTerms", exception.Message));
        }
    }

    private static SubscriptionLimit CreateLimit(SubscriptionLimitInput input)
    {
        return input.Kind switch
        {
            SubscriptionLimitKind.Finite when input.Value.HasValue => SubscriptionLimit.Finite(input.Value.Value),
            SubscriptionLimitKind.Unlimited when input.Value is null => SubscriptionLimit.Unlimited,
            _ => throw new DomainException("Subscription limit is invalid.")
        };
    }
}

public sealed class PublishSubscriptionPlanVersionCommandHandler
    : ICommandHandler<PublishSubscriptionPlanVersionCommand>
{
    private static readonly Error NotFound = new("Subscriptions.Plan.NotFound", "Subscription plan was not found.");
    private static readonly Error AlreadyPublished = new("Subscriptions.Plan.AlreadyPublished", "Subscription plan is already published.");
    private readonly IFamiliesDbContext _dbContext;

    public PublishSubscriptionPlanVersionCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result> Handle(PublishSubscriptionPlanVersionCommand request, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlanVersions
            .SingleOrDefaultAsync(item => item.Id == request.PlanVersionId, cancellationToken);
        if (plan is null) return Result.Failure(NotFound);
        if (plan.IsPublished || plan.PublishedOnUtc is not null) return Result.Failure(AlreadyPublished);

        try
        {
            plan.Publish(DateTime.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException)
        {
            return Result.Failure(AlreadyPublished);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyPublished);
        }

        return Result.Success();
    }
}

public sealed class CancelSubscriptionRenewalCommandHandler : ICommandHandler<CancelSubscriptionRenewalCommand>
{
    private static readonly Error SubscriptionNotFound = new("Subscriptions.Subscription.NotFound", "The current subscription was not found.");
    private static readonly Error AlreadyRequested = new("Subscriptions.CancelRenewal.AlreadyRequested", "Subscription renewal cancellation was already requested.");
    private readonly IFamiliesDbContext _db;
    public CancelSubscriptionRenewalCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result> Handle(CancelSubscriptionRenewalCommand request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result.Failure(FamilyErrors.NotOwner);
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, cancellationToken);
        if (subscription is null) return Result.Failure(SubscriptionNotFound);
        if (!subscription.AutoRenewEnabled || subscription.CancellationRequestedOnUtc is not null) return Result.Failure(AlreadyRequested);
        subscription.CancelRenewal(DateTime.UtcNow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRequested);
        }
        return Result.Success();
    }
}

public sealed class ReenableSubscriptionAutoRenewCommandHandler : ICommandHandler<ReenableSubscriptionAutoRenewCommand>
{
    private static readonly Error SubscriptionNotFound = new("Subscriptions.Subscription.NotFound", "The current subscription was not found.");
    private static readonly Error NotCancelled = new("Subscriptions.ReenableAutoRenew.NotCancelled", "Subscription auto-renew is already enabled.");
    private static readonly Error AlreadyRequested = new("Subscriptions.CancelRenewal.AlreadyRequested", "Subscription renewal cancellation was already requested.");
    private readonly IFamiliesDbContext _db;
    public ReenableSubscriptionAutoRenewCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result> Handle(ReenableSubscriptionAutoRenewCommand request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result.Failure(FamilyErrors.NotOwner);
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, cancellationToken);
        if (subscription is null) return Result.Failure(SubscriptionNotFound);
        if (subscription.CancellationRequestedOnUtc is null) return Result.Failure(NotCancelled);
        try
        {
            subscription.ReenableAutoRenew(DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception) when (exception.Message == "Subscription current period has ended.")
        {
            return Result.Failure(new Error("Subscriptions.ReenableAutoRenew.PeriodEnded", "The current subscription period has ended."));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRequested);
        }
        return Result.Success();
    }
}

public sealed record RetireSubscriptionPlanCommand(Guid PlanVersionId, UserId ActorUserId) : ICommand;

public sealed class RetireSubscriptionPlanCommandHandler : ICommandHandler<RetireSubscriptionPlanCommand>
{
    private static readonly Error NotFound = new("Subscriptions.Plan.NotFound", "Subscription plan was not found.");
    private static readonly Error NotPublished = new("Subscriptions.Plan.NotPublished", "Only a published subscription plan can be retired.");
    private static readonly Error AlreadyRetired = new("Subscriptions.Plan.AlreadyRetired", "Subscription plan is already retired.");
    private readonly IFamiliesDbContext _dbContext;

    public RetireSubscriptionPlanCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result> Handle(RetireSubscriptionPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlanVersions
            .SingleOrDefaultAsync(item => item.Id == request.PlanVersionId, cancellationToken);

        if (plan is null) return Result.Failure(NotFound);
        if (!plan.IsPublished) return Result.Failure(NotPublished);
        if (!plan.IsAvailableForNewSales) return Result.Failure(AlreadyRetired);

        var retiredOnUtc = DateTime.UtcNow;
        var audit = SubscriptionPlanRetirementAudit.Create(plan, request.ActorUserId, retiredOnUtc);
        plan.RetireFromNewSales();
        _dbContext.SubscriptionPlanRetirementAudits.Add(audit);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRetired);
        }

        return Result.Success();
    }
}
