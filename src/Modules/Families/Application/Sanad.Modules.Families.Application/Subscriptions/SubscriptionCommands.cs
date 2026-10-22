using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Families.Application.Subscriptions;

/// <summary>
/// Cancel-renewal. When an <paramref name="IdempotencyKey"/> is supplied the
/// handler also records the clause-9 cancel-renewal <em>outcome</em> — a
/// zero-piastre, non-refund movement (access continues through the paid period,
/// no partial-period refund) — through the Finance seam, so the cancel outcome
/// is attributable and distinct from a duplicate-charge refund and a shutdown
/// proration. The key is optional: a caller that does not need the recorded
/// outcome keeps the pre-existing byte-identical lifecycle, and Finance alone
/// persists the movement.
/// <para>
/// Replay (M3): a keyless retry of an already-cancelled renewal is still
/// <c>AlreadyRequested</c>. With a key, an already-cancelled renewal is
/// <em>not</em> short-circuited — the recorder is idempotent and
/// conflict-detecting, so the same key with the same content replays the
/// recorded outcome and a differing content yields the stable idempotency
/// conflict.
/// </para>
/// </summary>
public sealed record CancelSubscriptionRenewalCommand(
    UserId UserId,
    string? IdempotencyKey = null) : ICommand;
public sealed record ReenableSubscriptionAutoRenewCommand(UserId UserId) : ICommand;

public sealed record SubscriptionBenefitInput(
    SubscriptionBenefitKey Key,
    bool IsIncluded,
    string? ArabicLabel = null,
    string? EnglishLabel = null);
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
    UserId ActorUserId,
    int? PaymobSubscriptionPlanId = null,
    // Version-owned authored copy. Every value is optional: a version may be
    // created without any copy, and an existing version without copy keeps
    // reading an absent value. Nothing is generated or translated.
    string? ArabicName = null,
    string? EnglishName = null,
    string? ArabicDescription = null,
    string? EnglishDescription = null) : ICommand<Guid>;

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
                request.Benefits.Select(item => SubscriptionBenefit.Create(
                    item.Key,
                    item.IsIncluded,
                    item.ArabicLabel,
                    item.EnglishLabel)),
                CreateLimit(request.MemberLimit),
                CreateLimit(request.MonthlyBookingLimit),
                request.Rollover);
            var version = SubscriptionPlanVersion.Create(
                plan,
                paymobSubscriptionPlanId: request.PaymobSubscriptionPlanId,
                arabicName: request.ArabicName,
                englishName: request.EnglishName,
                arabicDescription: request.ArabicDescription,
                englishDescription: request.EnglishDescription);
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
    private readonly ISubscriptionRefundRecorder? _refunds;

    public CancelSubscriptionRenewalCommandHandler(IFamiliesDbContext db, ISubscriptionRefundRecorder? refunds = null)
    {
        _db = db;
        _refunds = refunds;
    }

    public async Task<Result> Handle(CancelSubscriptionRenewalCommand request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result.Failure(FamilyErrors.NotOwner);
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, cancellationToken);
        if (subscription is null) return Result.Failure(SubscriptionNotFound);

        bool alreadyCancelled = !subscription.AutoRenewEnabled || subscription.CancellationRequestedOnUtc is not null;

        // M3: a keyless retry of an already-cancelled renewal is still
        // AlreadyRequested. But when the caller supplied an idempotency key we do
        // NOT short-circuit: the recorder is idempotent and conflict-detecting, so
        // we route the replay through it and return the recorded outcome — the same
        // key with the same content replays the original, a differing content is
        // the stable idempotency conflict. This makes the cancel outcome
        // re-readable after the first commit.
        if (alreadyCancelled && string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure(AlreadyRequested);

        DateTime requestedOnUtc = subscription.CancellationRequestedOnUtc ?? DateTime.UtcNow;
        if (!alreadyCancelled)
        {
            subscription.CancelRenewal(requestedOnUtc);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(AlreadyRequested);
            }
        }

        // Clause 9: cancel-renewal retains access through the paid period and
        // refunds nothing for the current period. When the caller supplies an
        // idempotency key the handler records that zero-piastre outcome through
        // the Finance seam so the cancellation is attributable and distinct from
        // the other clause-9 reasons — the lifecycle above (when it ran) is already
        // committed, so a rejected recording never rolls the cancellation back; the
        // caller replays the same key to obtain the recorded outcome.
        if (_refunds is not null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            // M4: cancel-renewal records a zero-piastre movement, so it claims no
            // budget and names no capture. The subscription id is still passed for
            // audit correlation.
            Result<SubscriptionRefundSnapshot> recorded = await _refunds.RecordAsync(
                CancelRenewalCaseId(subscription.Id),
                family.Id.Value,
                subscription.Id,
                Guid.Empty,
                0,
                Sanad.Modules.Finance.Domain.Money.Zero,
                SubscriptionRefundReason.CancelRenewal,
                request.IdempotencyKey,
                requestedOnUtc,
                request.UserId.Value,
                evidenceReference: null,
                cancellationToken);

            if (recorded.IsFailure)
            {
                return Result.Failure(recorded.Error.Code == "Finance.SubscriptionRefund.IdempotencyConflict"
                    ? SubscriptionRefundErrors.IdempotencyConflict
                    : new Error(SubscriptionRefundErrors.FinanceRejected.Code, recorded.Error.Message));
            }
        }

        return Result.Success();
    }

    private static Guid CancelRenewalCaseId(Guid subscriptionId)
    {
        Span<byte> source = stackalloc byte[16];
        subscriptionId.TryWriteBytes(source);
        source[0] ^= 0xC7;
        return new Guid(source);
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
