using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPlanChangeTests
{
    [Fact]
    public async Task Quote_uses_tax_effective_remaining_target_gross_minus_settled_prorated_credit()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, start, end, SubscriptionPlan.PremiumPlus, currentGross: 299m, currentTaxRate: 15m);

        var result = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, target.Id, now), default);

        Assert.True(result.IsSuccess);
        decimal fraction = 16m / 31m;
        Assert.Equal(decimal.Round(2499m * 1.15m * fraction, 2, MidpointRounding.ToEven), result.Value.TargetRemainingGross);
        Assert.Equal(decimal.Round(299m * fraction, 2, MidpointRounding.ToEven), result.Value.SettledCredit);
        Assert.Equal(decimal.Round(Math.Max(0m, result.Value.TargetRemainingGross - result.Value.SettledCredit), 2), result.Value.TotalPayable);
        Assert.Equal(15m, result.Value.TaxRatePercentage);
    }

    [Fact]
    public async Task Quote_gives_no_credit_for_unsettled_current_period_and_rejects_non_owner_and_grace()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, start, end, SubscriptionPlan.PremiumPlus);
        var source = db.FamilySubscriptions.Single();
        source.BeginRenewalGrace(end.AddMinutes(1));
        await db.SaveChangesAsync();

        var nonOwner = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(UserId.New(), target.Id, now), default);
        var grace = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, target.Id, now), default);

        Assert.False(nonOwner.IsSuccess);
        Assert.Equal("Subscriptions.PlanChange.NotOwner", nonOwner.Error.Code);
        Assert.False(grace.IsSuccess);
        Assert.Equal("Subscriptions.PlanChange.RenewalGrace", grace.Error.Code);

    }

    [Fact]
    public async Task Quote_gives_no_credit_when_current_period_has_no_settled_gross()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, start, end, SubscriptionPlan.PremiumPlus);

        var result = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, target.Id, now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.SettledCredit);
    }

    [Fact]
    public async Task Wallet_upgrade_uses_manual_payment_and_does_not_update_provider_subscription()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target,
            now.AddDays(-15), now.AddDays(16), SubscriptionPlan.PremiumPlus, "provider-sub");
        var paymob = new FakePaymobClient();

        var intent = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Wallet, Billing(), now), default);
        Assert.True(intent.IsSuccess);
        Assert.Null(paymob.LastUpdatedAmount);

        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 103, (long)(intent.Value.Amount * 100m), "EGP", true, false, now), default);

        Assert.True(paid.IsSuccess);
        Assert.Equal("Paid", paid.Value.Outcome);
        Assert.Null(paymob.LastUpdatedAmount);
        Assert.Equal(target.Key, db.FamilySubscriptions.Single().PlanKey);
    }

    [Fact]
    public async Task Pending_downgrade_actions_are_owner_only()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target,
            now.AddDays(-15), now.AddDays(16), SubscriptionPlan.Premium, "provider-sub", sourcePlan: SubscriptionPlan.PremiumPlus);
        var paymob = new FakePaymobClient();
        UserId nonOwner = UserId.New();

        var replace = await new ReplacePendingDowngradeCommandHandler(db, paymob).Handle(
            new ReplacePendingDowngradeCommand(nonOwner, target.Id, now), default);
        var cancel = await new CancelPendingDowngradeCommandHandler(db, paymob).Handle(
            new CancelPendingDowngradeCommand(nonOwner, now), default);

        Assert.False(replace.IsSuccess);
        Assert.Equal("Subscriptions.PlanChange.NotOwner", replace.Error.Code);
        Assert.False(cancel.IsSuccess);
        Assert.Equal("Subscriptions.PlanChange.NotOwner", cancel.Error.Code);
        Assert.Null(db.FamilySubscriptions.Single().PendingDowngrade);
    }

    [Fact]
    public async Task Current_snapshot_exposes_pending_downgrade_without_changing_current_plan()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        await using var db = Seed(out Family family, out SubscriptionPlanVersion source, out SubscriptionPlanVersion target,
            start, end, SubscriptionPlan.Premium, sourcePlan: SubscriptionPlan.PremiumPlus);
        db.FamilySubscriptions.Single().ReplacePendingDowngrade(target);
        await db.SaveChangesAsync();

        var result = await new GetCurrentSubscriptionQueryHandler(db).Handle(
            new GetCurrentSubscriptionQuery(family.OwnerUserId), default);

        Assert.True(result.IsSuccess);
        var snapshot = result.Value.CurrentSubscription;
        Assert.NotNull(snapshot);
        Assert.Equal(source.Key, snapshot!.PlanKey);
        Assert.NotNull(snapshot.PendingDowngrade);
        Assert.Equal(target.Key, snapshot.PendingDowngrade!.PlanKey);
    }

    [Fact]
    public async Task Card_upgrade_callback_updates_provider_before_finalizing_local_snapshot()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, now.AddDays(-15), now.AddDays(16), SubscriptionPlan.PremiumPlus, "provider-sub", 1000m, 0m);
        var paymob = new FakePaymobClient();
        var intent = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Card, Billing(), now), default);

        Assert.True(intent.IsSuccess);
        Assert.Null(paymob.LastUpdatedAmount);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 101, (long)(intent.Value.Amount * 100m), "EGP", true, false, now), default);

        Assert.True(paid.IsSuccess);
        Assert.Equal("Paid", paid.Value.Outcome);
        Assert.Equal(2499m * 1.15m, paymob.LastUpdatedAmount);
        Assert.Equal(target.Key, db.FamilySubscriptions.Single().PlanKey);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Succeeded, db.SubscriptionPaymentAttempts.Single().Status);
    }

    [Fact]
    public async Task Provider_failure_does_not_finalize_paid_plan_change()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out SubscriptionPlanVersion source, out SubscriptionPlanVersion target, now.AddDays(-15), now.AddDays(16), SubscriptionPlan.PremiumPlus, "provider-sub");
        var paymob = new FakePaymobClient { UpdateFailure = true };
        var intent = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Card, Billing(), now), default);
        var before = db.FamilySubscriptions.Single().PlanKey;

        var result = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 102, (long)(intent.Value.Amount * 100m), "EGP", true, false, now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal(before, db.FamilySubscriptions.Single().PlanKey);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Pending, db.SubscriptionPaymentAttempts.Single().Status);
        Assert.Equal(source.Key, db.FamilySubscriptions.Single().PlanKey);
    }

    [Fact]
    public async Task Zero_charge_card_upgrade_updates_provider_and_finalizes_immediately()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, now.AddDays(-15), now.AddDays(16), SubscriptionPlan.PremiumPlus, "provider-sub", 10000m, 0m);
        var paymob = new FakePaymobClient();

        var result = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Card, Billing(), now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.Amount);
        Assert.Equal(2499m * 1.15m, paymob.LastUpdatedAmount);
        Assert.Equal(target.Key, db.FamilySubscriptions.Single().PlanKey);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Succeeded, db.SubscriptionPaymentAttempts.Single().Status);
    }

    [Fact]
    public async Task Quote_after_paid_card_upgrade_credits_persisted_target_remaining_gross()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target,
            start, end, SubscriptionPlan.PremiumPlus, "provider-sub", 1000m, 0m);
        SubscriptionPlanVersion followup = AddPlan(db, "ultra", 7000m, start);
        var initialQuote = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, target.Id, now), default);
        var paymob = new FakePaymobClient();

        var intent = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Card, Billing(), now), default);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 104, (long)(intent.Value.Amount * 100m), "EGP", true, false, now), default);
        var nextQuote = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, followup.Id, now), default);

        Assert.True(initialQuote.IsSuccess);
        Assert.True(paid.IsSuccess);
        Assert.True(nextQuote.IsSuccess);
        Assert.Equal(initialQuote.Value.TargetRemainingGross, db.FamilySubscriptions.Single().CurrentPeriodGross);
        decimal fraction = (decimal)(end - now).Ticks / (end - end.AddYears(-1)).Ticks;
        Assert.Equal(decimal.Round(initialQuote.Value.TargetRemainingGross * fraction, 2, MidpointRounding.ToEven), nextQuote.Value.SettledCredit);
        Assert.NotEqual(0m, nextQuote.Value.SettledCredit);
    }

    [Fact]
    public async Task Quote_after_zero_charge_upgrade_credits_persisted_target_remaining_gross()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target,
            start, end, SubscriptionPlan.PremiumPlus, "provider-sub", 10000m, 0m);
        SubscriptionPlanVersion followup = AddPlan(db, "ultra", 7000m, start);
        var initialQuote = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, target.Id, now), default);
        var paymob = new FakePaymobClient();

        var intent = await new CreatePlanChangePaymentIntentCommandHandler(db, paymob).Handle(
            new CreatePlanChangePaymentIntentCommand(family.OwnerUserId, target.Id, SubscriptionPaymentMethod.Card, Billing(), now), default);
        var nextQuote = await new CreatePlanChangeQuoteCommandHandler(db).Handle(
            new CreatePlanChangeQuoteCommand(family.OwnerUserId, followup.Id, now), default);

        Assert.True(initialQuote.IsSuccess);
        Assert.True(intent.IsSuccess);
        Assert.Equal(0m, intent.Value.Amount);
        Assert.True(nextQuote.IsSuccess);
        Assert.Equal(initialQuote.Value.TargetRemainingGross, db.FamilySubscriptions.Single().CurrentPeriodGross);
        decimal fraction = (decimal)(end - now).Ticks / (end - end.AddYears(-1)).Ticks;
        Assert.Equal(decimal.Round(initialQuote.Value.TargetRemainingGross * fraction, 2, MidpointRounding.ToEven), nextQuote.Value.SettledCredit);
    }

    [Fact]
    public async Task Pending_downgrade_card_replace_and_cancel_update_provider_before_local_mutation()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, now.AddDays(-15), now.AddDays(16), SubscriptionPlan.Premium, "provider-sub", sourcePlan: SubscriptionPlan.PremiumPlus);
        var paymob = new FakePaymobClient();

        var replace = await new ReplacePendingDowngradeCommandHandler(db, paymob).Handle(
            new ReplacePendingDowngradeCommand(family.OwnerUserId, target.Id, now), default);
        Assert.True(replace.IsSuccess);
        Assert.Equal(299m * 1.15m, paymob.LastUpdatedAmount);
        Assert.NotNull(db.FamilySubscriptions.Single().PendingDowngrade);

        var cancel = await new CancelPendingDowngradeCommandHandler(db, paymob).Handle(
            new CancelPendingDowngradeCommand(family.OwnerUserId, now), default);
        Assert.True(cancel.IsSuccess);
        Assert.Equal(2499m * 1.15m, paymob.LastUpdatedAmount);
        Assert.Null(db.FamilySubscriptions.Single().PendingDowngrade);
    }

    [Fact]
    public async Task Pending_downgrade_provider_failure_leaves_existing_pending_state_unchanged()
    {
        DateTime now = new(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Seed(out Family family, out _, out SubscriptionPlanVersion target, now.AddDays(-15), now.AddDays(16), SubscriptionPlan.Premium, "provider-sub", sourcePlan: SubscriptionPlan.PremiumPlus);
        var paymob = new FakePaymobClient();
        await new ReplacePendingDowngradeCommandHandler(db, paymob).Handle(new(family.OwnerUserId, target.Id, now), default);
        var existing = db.FamilySubscriptions.Single().PendingDowngrade;
        paymob.UpdateFailure = true;

        var result = await new CancelPendingDowngradeCommandHandler(db, paymob).Handle(new(family.OwnerUserId, now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.NotNull(db.FamilySubscriptions.Single().PendingDowngrade);
        Assert.Equal(existing!.PlanKey, db.FamilySubscriptions.Single().PendingDowngrade!.PlanKey);
    }

    [Fact]
    public void Successful_renewal_applies_pending_downgrade_at_original_anchor_and_clears_it()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        var current = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium, true, true, start, start);
        var target = SubscriptionPlanVersion.Create(SubscriptionPlan.Free, true, true, start, start);
        var subscription = FamilySubscription.Create(FamilyId.New(), current, start, end);
        subscription.ReplacePendingDowngrade(target);

        subscription.ApplySuccessfulRenewal(end.AddDays(3));

        Assert.Equal(target.Key, subscription.PlanKey);
        Assert.Equal(target.Price, subscription.Price);
        Assert.Null(subscription.PendingDowngrade);
        Assert.Equal(end.AddMonths(1), subscription.CurrentPeriodEndsOnUtc);
    }

    private static FamiliesDbContext Seed(
        out Family family,
        out SubscriptionPlanVersion source,
        out SubscriptionPlanVersion target,
        DateTime start,
        DateTime end,
        SubscriptionPlan targetPlan,
        string? providerId = null,
        decimal? currentGross = null,
        decimal currentTaxRate = 0m,
        SubscriptionPlan? sourcePlan = null)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        family = Family.Create(UserId.New());
        source = SubscriptionPlanVersion.Create(sourcePlan ?? SubscriptionPlan.Premium, true, true, start, start);
        target = SubscriptionPlanVersion.Create(targetPlan, true, true, start, start);
        var subscription = FamilySubscription.Create(family.Id, source, start, end);
        if (providerId is not null) subscription.AssociatePaymobSubscription(providerId, null, null, null);
        if (currentGross is not null) subscription.SetCurrentPeriodSettlement(currentGross.Value, currentTaxRate);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.AddRange(source, target);
        db.FamilySubscriptions.Add(subscription);
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(15m, 1, start, start));
        db.SaveChanges();
        return db;
    }

    private static PaymobBillingData Billing() => new("Ahmed", "Ali", "ahmed@example.com", "+201012345678");

    private static SubscriptionPlanVersion AddPlan(FamiliesDbContext db, string key, decimal price, DateTime createdOnUtc)
    {
        var plan = SubscriptionPlan.Create(
            key, 1, price, SubscriptionCycle.Annual, SubscriptionPlan.DefaultCurrency,
            SubscriptionPlan.PremiumPlus.Benefits, SubscriptionPlan.PremiumPlus.MemberLimit,
            SubscriptionPlan.PremiumPlus.MonthlyBookingLimit, SubscriptionRollover.NotApplicable);
        var version = SubscriptionPlanVersion.Create(plan, true, true, createdOnUtc, createdOnUtc);
        db.SubscriptionPlanVersions.Add(version);
        db.SaveChanges();
        return version;
    }

    private sealed class FakePaymobClient : IPaymobClient
    {
        public decimal? LastUpdatedAmount { get; private set; }
        public bool UpdateFailure { get; set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(PaymobPaymentIntentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(new Error("Paymob.MethodNotAvailable", "Not used.")));

        public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(PaymobSubscriptionPaymentIntentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent(input.MerchantReference, "order", "secret", "public")));

        public Task<Result> UpdateSubscriptionAmountAsync(string providerSubscriptionId, decimal targetRecurringGross, CancellationToken cancellationToken = default)
        {
            LastUpdatedAmount = targetRecurringGross;
            return Task.FromResult(UpdateFailure
                ? Result.Failure(new Error("Paymob.GatewayError", "Provider update failed."))
                : Result.Success());
        }

        public Task<Result<string?>> RefundPaymentAsync(string paymobTransactionId, decimal amount, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success(null));
    }
}
