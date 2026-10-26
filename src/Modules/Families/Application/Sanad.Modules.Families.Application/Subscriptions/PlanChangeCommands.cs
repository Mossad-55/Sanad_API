using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record PlanChangeQuoteResponse(Guid PlanVersionId, string PlanKey, int PlanVersion,
    decimal TargetRemainingGross, decimal SettledCredit, decimal TaxRatePercentage, decimal TargetTaxAmount, decimal PlatformFeeRatePercentage, decimal PlatformFeeAmount, int PlatformChargeRuleVersion, decimal TotalPayable, string Currency);

/// <summary>
/// Owner decision (mobile integration, 2026-10-26): an upgrade, downgrade or
/// re-purchase is not allowed before the current paid period ends.
/// <para>
/// An <b>immediate</b> plan change — the plan-change payment intent, which would
/// move the family to another plan right now and take a new payment — is refused
/// with <see cref="PaidPeriodActiveCode"/> (a documented <c>409</c>) while the
/// current subscription is a <b>paid</b> plan whose period has not yet ended. The
/// refusal happens before any provider call and before any payment attempt is
/// written, so no new payment is created.
/// </para>
/// <para>
/// The paid period is bounded by <see cref="FamilySubscription.CurrentPeriodEndsOnUtc"/>
/// alone. Auto-renew state is deliberately not consulted: turning auto-renew off
/// (cancel-renewal) neither ends the paid period nor lifts the block. A Free
/// snapshot (<c>Price == 0</c>) has no paid period to protect, so it may still
/// change plan.
/// </para>
/// <para>
/// A <b>deferred</b> change is not an immediate plan change: the scheduled
/// pending-downgrade is applied only at the original period boundary, so the plan
/// does not change before the current paid period ends and no payment is created.
/// The plan-change quote stays a read. Neither is gated here.
/// </para>
/// </summary>
internal static class PlanChangePolicy
{
    /// <summary>Documented <c>409</c>: the current paid period is still running.</summary>
    internal const string PaidPeriodActiveCode = "Subscriptions.PlanChange.PaidPeriodActive";

    /// <summary>
    /// Documented <c>409</c>: a plan-change payment for this subscription is
    /// already awaiting settlement, so a duplicate or second-device attempt must
    /// not create another payment.
    /// </summary>
    internal const string PaymentPendingCode = "Subscriptions.PlanChange.PaymentPending";

    internal static Error PaidPeriodActive => new(
        PaidPeriodActiveCode,
        "The subscription plan cannot be changed before the current paid period ends.");

    internal static Error PaymentPending => new(
        PaymentPendingCode,
        "A plan-change payment for this subscription is already pending.");

    /// <summary>
    /// True while the current subscription is a paid plan whose period has not
    /// yet ended — the interval in which no immediate plan change is allowed.
    /// </summary>
    internal static bool IsPaidPeriodActive(FamilySubscription subscription, DateTime nowUtc) =>
        subscription.Price > 0m && nowUtc < subscription.CurrentPeriodEndsOnUtc;
}
public sealed record CreatePlanChangeQuoteCommand(UserId UserId, Guid PlanVersionId, DateTime UtcNow) : IQuery<PlanChangeQuoteResponse>;
public sealed record CreatePlanChangePaymentIntentCommand(UserId UserId, Guid PlanVersionId, SubscriptionPaymentMethod Method,
    PaymobBillingData Billing, DateTime UtcNow) : ICommand<SubscriptionPaymentIntentResponse>;
public sealed record ReplacePendingDowngradeCommand(UserId UserId, Guid PlanVersionId, DateTime UtcNow) : ICommand;
public sealed record CancelPendingDowngradeCommand(UserId UserId, DateTime UtcNow) : ICommand;

public sealed class CreatePlanChangePaymentIntentCommandValidator : AbstractValidator<CreatePlanChangePaymentIntentCommand>
{
    public CreatePlanChangePaymentIntentCommandValidator()
    {
        RuleFor(x => x.Method).IsInEnum(); RuleFor(x => x.Billing.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Billing.LastName).NotEmpty().MaximumLength(100); RuleFor(x => x.Billing.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Billing.PhoneNumber).NotEmpty().Matches(@"^\+?\d{8,15}$");
    }
}

public sealed class CreatePlanChangeQuoteCommandHandler : IQueryHandler<CreatePlanChangeQuoteCommand, PlanChangeQuoteResponse>
{
    private readonly IFamiliesDbContext _db;
    private readonly IPlatformChargeRuleReader _chargeRules;
    public CreatePlanChangeQuoteCommandHandler(IFamiliesDbContext db, IPlatformChargeRuleReader chargeRules) { _db = db; _chargeRules = chargeRules; }
    public async Task<Result<PlanChangeQuoteResponse>> Handle(CreatePlanChangeQuoteCommand request, CancellationToken ct)
    {
        var (subscription, plan, error) = await Resolve(request.UserId, request.PlanVersionId, request.UtcNow, ct);
        if (error is not null) return Result<PlanChangeQuoteResponse>.Failure(error);
        var charge = await _chargeRules.GetEffectiveAsync(request.UtcNow, ct);
        if (charge is null) return Result<PlanChangeQuoteResponse>.Failure(new Error("Subscriptions.Quote.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));
        decimal fraction = RemainingFraction(subscription!, request.UtcNow);
        decimal targetFee = Money((plan!.Price * charge.PlatformFeeRatePercentage / 100m) * fraction);
        decimal targetTax = Money((plan.Price * charge.TaxRatePercentage / 100m) * fraction);
        decimal targetGross = Money((plan.Price + plan.Price * charge.PlatformFeeRatePercentage / 100m + plan.Price * charge.TaxRatePercentage / 100m) * fraction);
        decimal credit = subscription!.CurrentPeriodGross is decimal gross ? Money(gross * fraction) : 0m;
        return new PlanChangeQuoteResponse(plan.Id, plan.Key, plan.Version, targetGross, credit, Money(charge.TaxRatePercentage), targetTax, Money(charge.PlatformFeeRatePercentage), targetFee, charge.Version, Money(Math.Max(0m, targetGross - credit)), plan.Currency);
    }
    /// <summary>
    /// Resolves the owner, current subscription and target plan for a plan-change
    /// request. It is shared by the quote read, the immediate plan-change intent
    /// and the deferred pending-downgrade lifecycle, so it deliberately does not
    /// apply the "no immediate plan change during a paid period" policy: that gate
    /// belongs to the immediate path alone and is applied by
    /// <see cref="CreatePlanChangePaymentIntentCommandHandler"/> via
    /// <see cref="PlanChangePolicy.IsPaidPeriodActive"/>.
    /// </summary>
    internal async Task<(FamilySubscription? Subscription, SubscriptionPlanVersion? Plan, Error? Error)> Resolve(UserId userId, Guid planId, DateTime now, CancellationToken ct)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, userId, ct);
        if (family is null || !FamilyAccess.IsOwner(family, userId)) return (null, null, new Error("Subscriptions.PlanChange.NotOwner", "Only the family owner can change a subscription plan."));
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, ct);
        if (subscription is null) return (null, null, new Error("Subscriptions.PlanChange.NotFound", "The current subscription was not found."));
        if (subscription.CancellationRequestedOnUtc is not null) return (null, null, new Error("Subscriptions.PlanChange.CancelRenewalRequested", "Plan changes are unavailable after renewal cancellation is requested."));
        if (subscription.IsWithinRenewalGrace(now)) return (null, null, new Error("Subscriptions.PlanChange.RenewalGrace", "Plan changes are unavailable during renewal grace."));
        var plan = await _db.SubscriptionPlanVersions.SingleOrDefaultAsync(x => x.Id == planId && x.IsPublished && x.IsAvailableForNewSales, ct);
        return plan is null ? (null, null, new Error("Subscriptions.PlanChange.PlanNotFound", "The requested subscription plan is not available.")) : (subscription, plan, null);
    }
    internal static decimal RemainingFraction(FamilySubscription s, DateTime now)
    {
        DateTime start = s.Cycle == SubscriptionCycle.Monthly ? s.CurrentPeriodEndsOnUtc.AddMonths(-1) : s.CurrentPeriodEndsOnUtc.AddYears(-1);
        if (now >= s.CurrentPeriodEndsOnUtc) return 0m;
        if (now <= start) return 1m;
        return (decimal)(s.CurrentPeriodEndsOnUtc - now).Ticks / (s.CurrentPeriodEndsOnUtc - start).Ticks;
    }
    internal static decimal Money(decimal x) => decimal.Round(x, 2, MidpointRounding.ToEven);
}

public sealed class CreatePlanChangePaymentIntentCommandHandler : ICommandHandler<CreatePlanChangePaymentIntentCommand, SubscriptionPaymentIntentResponse>
{
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob; private readonly IPlatformChargeRuleReader _chargeRules;
    private readonly IPlatformControlReader _controls; private readonly IPaymentMethodConfiguration _paymentMethods;
    public CreatePlanChangePaymentIntentCommandHandler(IFamiliesDbContext db, IPaymobClient paymob, IPlatformChargeRuleReader chargeRules, IPlatformControlReader controls, IPaymentMethodConfiguration paymentMethods) { _db = db; _paymob = paymob; _chargeRules = chargeRules; _controls = controls; _paymentMethods = paymentMethods; }
    public async Task<Result<SubscriptionPaymentIntentResponse>> Handle(CreatePlanChangePaymentIntentCommand request, CancellationToken ct)
    {
        // E04: a new plan-change payment intent is gated before any provider
        // call or attempt write. The plan-change quote stays a read, and
        // pending-downgrade replace/cancel stay lifecycle operations.
        Error? platformError = await PlatformControlGate.CheckNewPaymentIntentAsync(
            _controls, _paymentMethods, request.Method, ct);
        if (platformError is not null) return Result<SubscriptionPaymentIntentResponse>.Failure(platformError);

        var planChange = new CreatePlanChangeQuoteCommandHandler(_db, _chargeRules);
        var quote = await planChange.Handle(new(request.UserId, request.PlanVersionId, request.UtcNow), ct);
        if (!quote.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(quote.Error);
        var resolved = await planChange.Resolve(request.UserId, request.PlanVersionId, request.UtcNow, ct);
        if (resolved.Error is not null) return Result<SubscriptionPaymentIntentResponse>.Failure(resolved.Error);
        // Mobile integration 2026-10-26: an immediate plan change is refused while
        // the current subscription's paid period is still running. The refusal
        // happens before any provider call and before any attempt write, so no new
        // payment is created. The quote read above and the deferred
        // pending-downgrade lifecycle are deliberately not gated: the downgrade
        // applies only at the period boundary and takes no payment.
        if (PlanChangePolicy.IsPaidPeriodActive(resolved.Subscription!, request.UtcNow))
            return Result<SubscriptionPaymentIntentResponse>.Failure(PlanChangePolicy.PaidPeriodActive);
        // A duplicate or second-device attempt must not create a second payment
        // for the same plan change; the pending attempt is the arbiter.
        if (await _db.SubscriptionPaymentAttempts.AnyAsync(
                attempt => attempt.SubscriptionId == resolved.Subscription!.Id
                    && attempt.IsPlanChange
                    && attempt.Status == SubscriptionPaymentAttemptStatus.Pending,
                ct))
            return Result<SubscriptionPaymentIntentResponse>.Failure(PlanChangePolicy.PaymentPending);
        if (resolved.Plan!.Price <= resolved.Subscription!.Price)
            return Result<SubscriptionPaymentIntentResponse>.Failure(new Error("Subscriptions.PlanChange.NotUpgrade", "Use the pending downgrade operation for a lower-priced plan."));
        if (quote.Value.TotalPayable <= 0m)
        {
            if (request.Method == SubscriptionPaymentMethod.Card)
            {
                if (string.IsNullOrWhiteSpace(resolved.Subscription.PaymobSubscriptionId))
                    return Result<SubscriptionPaymentIntentResponse>.Failure(new Error("Paymob.SubscriptionNotFound", "No provider subscription is available to update."));
                decimal futureGross = CreatePlanChangeQuoteCommandHandler.Money(
                    resolved.Plan.Price * (1m
                        + quote.Value.PlatformFeeRatePercentage / 100m
                        + quote.Value.TaxRatePercentage / 100m));
                var update = await _paymob.UpdateSubscriptionAmountAsync(resolved.Subscription.PaymobSubscriptionId, futureGross, ct);
                if (!update.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(update.Error);
            }
            var zeroAttempt = SubscriptionPaymentAttempt.CreatePlanChange(resolved.Subscription, resolved.Plan, quote.Value.TargetRemainingGross,
                resolved.Subscription.CurrentPeriodGross ?? 0m, quote.Value.SettledCredit, quote.Value.TaxRatePercentage, quote.Value.TargetTaxAmount, request.Method, request.UtcNow);
            zeroAttempt.CapturePlatformCharge(quote.Value.PlatformFeeRatePercentage, quote.Value.PlatformFeeAmount, quote.Value.PlatformChargeRuleVersion);
            zeroAttempt.TryMarkSucceeded("no-charge", request.UtcNow);
            resolved.Subscription.ApplyPaidPlanChange(resolved.Plan, zeroAttempt);
            _db.SubscriptionPaymentAttempts.Add(zeroAttempt);
            await _db.SaveChangesAsync(ct);
            return new SubscriptionPaymentIntentResponse(zeroAttempt.Id, zeroAttempt.MerchantReference, zeroAttempt.Method, 0m, zeroAttempt.Currency, string.Empty, string.Empty, false);
        }
        var attempt = SubscriptionPaymentAttempt.CreatePlanChange(resolved.Subscription!, resolved.Plan!, quote.Value.TargetRemainingGross,
            resolved.Subscription!.CurrentPeriodGross ?? 0m, quote.Value.SettledCredit, quote.Value.TaxRatePercentage, quote.Value.TargetTaxAmount, request.Method, request.UtcNow);
        attempt.CapturePlatformCharge(quote.Value.PlatformFeeRatePercentage, quote.Value.PlatformFeeAmount, quote.Value.PlatformChargeRuleVersion);
        var intent = await _paymob.CreateSubscriptionPaymentIntentAsync(new(attempt.MerchantReference, request.Method, attempt.TotalPayable, attempt.Currency, request.Billing), ct);
        if (!intent.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(intent.Error);
        attempt.RecordPaymobOrder(intent.Value.PaymobOrderId); _db.SubscriptionPaymentAttempts.Add(attempt); await _db.SaveChangesAsync(ct);
        return new SubscriptionPaymentIntentResponse(attempt.Id, attempt.MerchantReference, attempt.Method, attempt.TotalPayable, attempt.Currency, intent.Value.ClientSecret, intent.Value.PublicKey, false);
    }
}

public sealed class ReplacePendingDowngradeCommandHandler : ICommandHandler<ReplacePendingDowngradeCommand>
{
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob; private readonly IPlatformChargeRuleReader _chargeRules;
    public ReplacePendingDowngradeCommandHandler(IFamiliesDbContext db, IPaymobClient paymob, IPlatformChargeRuleReader chargeRules) { _db = db; _paymob = paymob; _chargeRules = chargeRules; }
    public async Task<Result> Handle(ReplacePendingDowngradeCommand r, CancellationToken ct)
    {
        // Mobile integration 2026-10-26: a downgrade is scheduled for the next
        // successful renewal, so the plan does not change before the current paid
        // period ends and no payment is created — it is the compliant way to
        // change plan under the policy ("no plan change before the period ends"),
        // so it is deliberately not gated by PlanChangePolicy.
        var resolved = await new CreatePlanChangeQuoteCommandHandler(_db, _chargeRules).Resolve(r.UserId, r.PlanVersionId, r.UtcNow, ct);
        if (resolved.Error is not null) return Result.Failure(resolved.Error);
        if (resolved.Plan!.Price >= resolved.Subscription!.Price) return Result.Failure(new Error("Subscriptions.PendingDowngrade.NotDowngrade", "The selected plan is not a downgrade."));
        if (resolved.Subscription.PaymobSubscriptionId is not null)
        {
            var charge = await _chargeRules.GetEffectiveAsync(r.UtcNow, ct);
            if (charge is null) return Result.Failure(new Error("Subscriptions.Quote.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));
            var update = await _paymob.UpdateSubscriptionAmountAsync(resolved.Subscription.PaymobSubscriptionId, CreatePlanChangeQuoteCommandHandler.Money(resolved.Plan.Price * (1m + charge.PlatformFeeRatePercentage / 100m + charge.TaxRatePercentage / 100m)), ct);
            if (!update.IsSuccess) return Result.Failure(update.Error);
        }
        resolved.Subscription.ReplacePendingDowngrade(resolved.Plan); await _db.SaveChangesAsync(ct); return Result.Success();
    }
}
public sealed class CancelPendingDowngradeCommandHandler : ICommandHandler<CancelPendingDowngradeCommand>
{
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob; private readonly IPlatformChargeRuleReader _chargeRules;
    public CancelPendingDowngradeCommandHandler(IFamiliesDbContext db, IPaymobClient paymob, IPlatformChargeRuleReader chargeRules) { _db = db; _paymob = paymob; _chargeRules = chargeRules; }
    public async Task<Result> Handle(CancelPendingDowngradeCommand r, CancellationToken ct)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, r.UserId, ct);
        if (family is null || !FamilyAccess.IsOwner(family, r.UserId)) return Result.Failure(new Error("Subscriptions.PlanChange.NotOwner", "Only the family owner can change a subscription plan."));
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, ct);
        if (subscription is null) return Result.Failure(new Error("Subscriptions.PlanChange.NotFound", "The current subscription was not found."));
        if (subscription.CancellationRequestedOnUtc is not null) return Result.Failure(new Error("Subscriptions.PlanChange.CancelRenewalRequested", "Plan changes are unavailable after renewal cancellation is requested."));
        if (subscription.IsWithinRenewalGrace(r.UtcNow)) return Result.Failure(new Error("Subscriptions.PlanChange.RenewalGrace", "Plan changes are unavailable during renewal grace."));
        if (subscription.PendingDowngrade is not null && subscription.PaymobSubscriptionId is not null)
        {
            var charge = await _chargeRules.GetEffectiveAsync(r.UtcNow, ct);
            if (charge is null) return Result.Failure(new Error("Subscriptions.Quote.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));
            var update = await _paymob.UpdateSubscriptionAmountAsync(subscription.PaymobSubscriptionId, CreatePlanChangeQuoteCommandHandler.Money(subscription.Price * (1m + charge.PlatformFeeRatePercentage / 100m + charge.TaxRatePercentage / 100m)), ct);
            if (!update.IsSuccess) return Result.Failure(update.Error);
        }
        subscription.CancelPendingDowngrade(); await _db.SaveChangesAsync(ct); return Result.Success();
    }
}
