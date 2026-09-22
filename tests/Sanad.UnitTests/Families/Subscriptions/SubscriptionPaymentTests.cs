using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPaymentTests
{
    [Fact]
    public async Task Owner_can_start_card_payment_from_server_quote_and_pending_attempt_is_persisted()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var now = DateTime.UtcNow;

        var result = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId,
                plan.Id,
                null,
                SubscriptionPaymentMethod.Card,
                Billing(),
                now),
            default);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("sub_", result.Value.MerchantReference);
        Assert.Equal(299m, result.Value.Amount);
        Assert.Equal("EGP", result.Value.Currency);
        Assert.False(result.Value.RecurringRenewalSupported);
        Assert.Equal(SubscriptionPaymentMethod.Card, paymob.LastInput!.Method);
        Assert.Equal(299m, paymob.LastInput.Amount);
        Assert.Equal("EGP", paymob.LastInput.Currency);
        Assert.Equal(result.Value.MerchantReference, paymob.LastInput.MerchantReference);

        var attempt = await db.SubscriptionPaymentAttempts.SingleAsync();
        Assert.Equal(result.Value.MerchantReference, attempt.MerchantReference);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Pending, attempt.Status);
        Assert.NotEqual(family.Id.Value.ToString(), attempt.MerchantReference);
    }

    [Fact]
    public async Task Wallet_is_initial_one_time_boundary_and_current_subscription_is_rejected()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var result = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Wallet, Billing(), DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionPaymentMethod.Wallet, paymob.LastInput!.Method);
        Assert.False(result.Value.RecurringRenewalSupported);

        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await db.SaveChangesAsync();
        result = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Payment.CurrentExists", result.Error.Code);
    }

    [Fact]
    public async Task Failed_and_pending_webhooks_do_not_activate_and_success_activates_once()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var intent = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);
        string reference = intent.Value.MerchantReference;

        var pending = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(reference, 10, 29900, "EGP", false, true, DateTime.UtcNow), default);
        Assert.Equal("Pending", pending.Value.Outcome);
        Assert.Empty(db.FamilySubscriptions);

        var failed = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(reference, 11, 29900, "EGP", false, false, DateTime.UtcNow), default);
        Assert.Equal("Failed", failed.Value.Outcome);
        Assert.Empty(db.FamilySubscriptions);

        var secondIntent = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow), default);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(secondIntent.Value.MerchantReference, 12, 29900, "EGP", true, false, DateTime.UtcNow), default);
        var duplicate = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(secondIntent.Value.MerchantReference, 13, 29900, "EGP", true, false, DateTime.UtcNow), default);

        Assert.Equal("Paid", paid.Value.Outcome);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);
        Assert.Single(db.FamilySubscriptions);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Succeeded, db.SubscriptionPaymentAttempts.Single(x => x.Id == secondIntent.Value.PaymentAttemptId).Status);
    }

    [Fact]
    public async Task Webhook_amount_or_currency_mismatch_is_rejected_without_activation()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var intent = await new CreateSubscriptionPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow), default);

        var amountMismatch = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 20, 1, "EGP", true, false, DateTime.UtcNow), default);
        var currencyMismatch = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 21, 29900, "USD", true, false, DateTime.UtcNow), default);

        Assert.False(amountMismatch.IsSuccess);
        Assert.Equal("Paymob.AmountMismatch", amountMismatch.Error.Code);
        Assert.False(currencyMismatch.IsSuccess);
        Assert.Equal("Paymob.AmountMismatch", currencyMismatch.Error.Code);
        Assert.Empty(db.FamilySubscriptions);
    }

    [Fact]
    public async Task Renewal_payment_starts_only_at_the_period_boundary_and_marks_attempt_as_renewal()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1));
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();

        var early = await new CreateSubscriptionRenewalPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), created.AddDays(15)), default);
        Assert.False(early.IsSuccess);
        Assert.Equal("Subscriptions.Renewal.NotDue", early.Error.Code);

        var result = await new CreateSubscriptionRenewalPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), created.AddMonths(1)), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RecurringRenewalSupported);
        var attempt = await db.SubscriptionPaymentAttempts.SingleAsync();
        Assert.True(attempt.IsRenewal);
        Assert.Equal(subscription.Id, attempt.SubscriptionId);
    }

    [Fact]
    public async Task Failed_renewal_enters_grace_and_successful_retry_preserves_anchor()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = created.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, created, periodEnd);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();
        DateTime failureTime = periodEnd.AddMinutes(1);

        var first = await new CreateSubscriptionRenewalPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), failureTime), default);
        var failed = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(first.Value.MerchantReference, 31, 29900, "EGP", false, false, failureTime), default);

        Assert.Equal("Failed", failed.Value.Outcome);
        Assert.True(subscription.IsWithinRenewalGrace(periodEnd.AddDays(3)));

        var retry = await new CreateSubscriptionRenewalPaymentIntentCommandHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), periodEnd.AddDays(3)), default);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db).Handle(
            new ConfirmSubscriptionPaymentCommand(retry.Value.MerchantReference, 32, 29900, "EGP", true, false, periodEnd.AddDays(3)), default);

        Assert.Equal("Paid", paid.Value.Outcome);
        Assert.Equal(periodEnd.AddMonths(1), subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
    }

    private static FamiliesDbContext SeedDb(out Family family, out SubscriptionPlanVersion plan)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DateTime now = DateTime.UtcNow;
        family = Family.Create(UserId.New());
        plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium, true, true, now.AddDays(-1), now.AddDays(-1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.SubscriptionTaxRules.Add(SubscriptionTaxRule.Create(0m, 1, now.AddDays(-1), now.AddDays(-1)));
        db.SaveChanges();
        return db;
    }

    private static PaymobBillingData Billing() =>
        new("Ahmed", "Ali", "ahmed@example.com", "+201012345678");

    private sealed class FakePaymobClient : IPaymobClient
    {
        public PaymobSubscriptionPaymentIntentInput? LastInput { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.MethodNotAvailable", "Booking path not used.")));

        public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
            PaymobSubscriptionPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(input.MerchantReference, "sub-intention", "sub-secret", "pk_test")));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success(null));
    }
}
