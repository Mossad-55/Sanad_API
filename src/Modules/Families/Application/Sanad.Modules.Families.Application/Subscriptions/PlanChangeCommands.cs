using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record PlanChangeQuoteResponse(Guid PlanVersionId, string PlanKey, int PlanVersion,
    decimal TargetRemainingGross, decimal SettledCredit, decimal TaxRatePercentage, decimal TargetTaxAmount, decimal TotalPayable, string Currency);
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
    public CreatePlanChangeQuoteCommandHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<PlanChangeQuoteResponse>> Handle(CreatePlanChangeQuoteCommand request, CancellationToken ct)
    {
        var (subscription, plan, error) = await Resolve(request.UserId, request.PlanVersionId, request.UtcNow, ct);
        if (error is not null) return Result<PlanChangeQuoteResponse>.Failure(error);
        var tax = await _db.SubscriptionTaxRules.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.EffectiveOnUtc <= request.UtcNow, ct);
        if (tax is null) return Result<PlanChangeQuoteResponse>.Failure(new Error("Subscriptions.Quote.TaxNotConfigured", "Subscription tax configuration is not available."));
        decimal fraction = RemainingFraction(subscription!, request.UtcNow);
        decimal targetGross = Money((plan!.Price * (1m + tax.RatePercentage / 100m)) * fraction);
        decimal targetTax = Money((plan.Price * tax.RatePercentage / 100m) * fraction);
        decimal credit = subscription!.CurrentPeriodGross is decimal gross ? Money(gross * fraction) : 0m;
        return new PlanChangeQuoteResponse(plan.Id, plan.Key, plan.Version, targetGross, credit, Money(tax.RatePercentage), targetTax, Money(Math.Max(0m, targetGross - credit)), plan.Currency);
    }
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
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob;
    public CreatePlanChangePaymentIntentCommandHandler(IFamiliesDbContext db, IPaymobClient paymob) { _db = db; _paymob = paymob; }
    public async Task<Result<SubscriptionPaymentIntentResponse>> Handle(CreatePlanChangePaymentIntentCommand request, CancellationToken ct)
    {
        var quote = await new CreatePlanChangeQuoteCommandHandler(_db).Handle(new(request.UserId, request.PlanVersionId, request.UtcNow), ct);
        if (!quote.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(quote.Error);
        var resolved = await new CreatePlanChangeQuoteCommandHandler(_db).Resolve(request.UserId, request.PlanVersionId, request.UtcNow, ct);
        if (resolved.Error is not null) return Result<SubscriptionPaymentIntentResponse>.Failure(resolved.Error);
        if (resolved.Plan!.Price <= resolved.Subscription!.Price)
            return Result<SubscriptionPaymentIntentResponse>.Failure(new Error("Subscriptions.PlanChange.NotUpgrade", "Use the pending downgrade operation for a lower-priced plan."));
        if (quote.Value.TotalPayable <= 0m)
        {
            if (request.Method == SubscriptionPaymentMethod.Card)
            {
                if (string.IsNullOrWhiteSpace(resolved.Subscription.PaymobSubscriptionId))
                    return Result<SubscriptionPaymentIntentResponse>.Failure(new Error("Paymob.SubscriptionNotFound", "No provider subscription is available to update."));
                decimal futureGross = CreatePlanChangeQuoteCommandHandler.Money(resolved.Plan.Price * (1m + quote.Value.TaxRatePercentage / 100m));
                var update = await _paymob.UpdateSubscriptionAmountAsync(resolved.Subscription.PaymobSubscriptionId, futureGross, ct);
                if (!update.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(update.Error);
            }
            var zeroAttempt = SubscriptionPaymentAttempt.CreatePlanChange(resolved.Subscription, resolved.Plan, quote.Value.TargetRemainingGross,
                resolved.Subscription.CurrentPeriodGross ?? 0m, quote.Value.SettledCredit, quote.Value.TaxRatePercentage, quote.Value.TargetTaxAmount, request.Method, request.UtcNow);
            zeroAttempt.TryMarkSucceeded("no-charge", request.UtcNow);
            resolved.Subscription.ApplyImmediatePlanChange(resolved.Plan, quote.Value.TargetRemainingGross, quote.Value.TaxRatePercentage);
            _db.SubscriptionPaymentAttempts.Add(zeroAttempt);
            await _db.SaveChangesAsync(ct);
            return new SubscriptionPaymentIntentResponse(zeroAttempt.Id, zeroAttempt.MerchantReference, zeroAttempt.Method, 0m, zeroAttempt.Currency, string.Empty, string.Empty, false);
        }
        var attempt = SubscriptionPaymentAttempt.CreatePlanChange(resolved.Subscription!, resolved.Plan!, quote.Value.TargetRemainingGross,
            resolved.Subscription!.CurrentPeriodGross ?? 0m, quote.Value.SettledCredit, quote.Value.TaxRatePercentage, quote.Value.TargetTaxAmount, request.Method, request.UtcNow);
        var intent = await _paymob.CreateSubscriptionPaymentIntentAsync(new(attempt.MerchantReference, request.Method, attempt.TotalPayable, attempt.Currency, request.Billing), ct);
        if (!intent.IsSuccess) return Result<SubscriptionPaymentIntentResponse>.Failure(intent.Error);
        attempt.RecordPaymobOrder(intent.Value.PaymobOrderId); _db.SubscriptionPaymentAttempts.Add(attempt); await _db.SaveChangesAsync(ct);
        return new SubscriptionPaymentIntentResponse(attempt.Id, attempt.MerchantReference, attempt.Method, attempt.TotalPayable, attempt.Currency, intent.Value.ClientSecret, intent.Value.PublicKey, false);
    }
}

public sealed class ReplacePendingDowngradeCommandHandler : ICommandHandler<ReplacePendingDowngradeCommand>
{
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob;
    public ReplacePendingDowngradeCommandHandler(IFamiliesDbContext db, IPaymobClient paymob) { _db = db; _paymob = paymob; }
    public async Task<Result> Handle(ReplacePendingDowngradeCommand r, CancellationToken ct)
    {
        var resolved = await new CreatePlanChangeQuoteCommandHandler(_db).Resolve(r.UserId, r.PlanVersionId, r.UtcNow, ct);
        if (resolved.Error is not null) return Result.Failure(resolved.Error);
        if (resolved.Plan!.Price >= resolved.Subscription!.Price) return Result.Failure(new Error("Subscriptions.PendingDowngrade.NotDowngrade", "The selected plan is not a downgrade."));
        if (resolved.Subscription.PaymobSubscriptionId is not null)
        {
            var tax = await _db.SubscriptionTaxRules.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.EffectiveOnUtc <= r.UtcNow, ct);
            if (tax is null) return Result.Failure(new Error("Subscriptions.Quote.TaxNotConfigured", "Subscription tax configuration is not available."));
            var update = await _paymob.UpdateSubscriptionAmountAsync(resolved.Subscription.PaymobSubscriptionId, CreatePlanChangeQuoteCommandHandler.Money(resolved.Plan.Price * (1m + tax.RatePercentage / 100m)), ct);
            if (!update.IsSuccess) return Result.Failure(update.Error);
        }
        resolved.Subscription.ReplacePendingDowngrade(resolved.Plan); await _db.SaveChangesAsync(ct); return Result.Success();
    }
}
public sealed class CancelPendingDowngradeCommandHandler : ICommandHandler<CancelPendingDowngradeCommand>
{
    private readonly IFamiliesDbContext _db; private readonly IPaymobClient _paymob;
    public CancelPendingDowngradeCommandHandler(IFamiliesDbContext db, IPaymobClient paymob) { _db = db; _paymob = paymob; }
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
            var tax = await _db.SubscriptionTaxRules.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.EffectiveOnUtc <= r.UtcNow, ct);
            if (tax is null) return Result.Failure(new Error("Subscriptions.Quote.TaxNotConfigured", "Subscription tax configuration is not available."));
            var update = await _paymob.UpdateSubscriptionAmountAsync(subscription.PaymobSubscriptionId, CreatePlanChangeQuoteCommandHandler.Money(subscription.Price * (1m + tax.RatePercentage / 100m)), ct);
            if (!update.IsSuccess) return Result.Failure(update.Error);
        }
        subscription.CancelPendingDowngrade(); await _db.SaveChangesAsync(ct); return Result.Success();
    }
}
