using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Payments;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Domain;
using Sanad.UnitTests.Finance;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPaymentTests
{
    [Fact]
    public async Task Owner_can_start_card_payment_from_server_quote_and_pending_attempt_is_persisted()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var now = DateTime.UtcNow;

        var result = await IntentHandler(db, paymob).Handle(
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
        Assert.Equal(343.85m, result.Value.Amount);
        Assert.Equal("EGP", result.Value.Currency);
        Assert.False(result.Value.RecurringRenewalSupported);
        Assert.Equal(SubscriptionPaymentMethod.Card, paymob.LastInput!.Method);
        Assert.Equal(343.85m, paymob.LastInput.Amount);
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
        var result = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Wallet, Billing(), DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionPaymentMethod.Wallet, paymob.LastInput!.Method);
        Assert.False(result.Value.RecurringRenewalSupported);

        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await db.SaveChangesAsync();
        result = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Payment.CurrentExists", result.Error.Code);
    }

    [Fact]
    public async Task Non_owner_cannot_start_payment_and_provider_is_not_called()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();

        var result = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                UserId.New(), plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Payment.NotOwner", result.Error.Code);
        Assert.Null(paymob.LastInput);
    }

    [Fact]
    public async Task Wallet_intention_omits_provider_plan_mapping_and_reports_no_recurrence()
    {
        var handler = new RecordingHttpMessageHandler();
        var client = new PaymobClient(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                SecretKey = "sk_test_secret",
                PublicKey = "pk_test",
                WalletIntegrationId = "123456"
            }));

        var result = await client.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                "sub_wallet_attempt",
                SubscriptionPaymentMethod.Wallet,
                299m,
                "EGP",
                Billing(),
                6755));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RecurringRenewalSupported);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(123456, body.RootElement.GetProperty("payment_methods")[0].GetInt64());
        Assert.False(body.RootElement.TryGetProperty("subscription_plan_id", out _));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Subscription_intention_without_secret_makes_no_provider_call()
    {
        var handler = new RecordingHttpMessageHandler();
        var client = new PaymobClient(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions { Card3dsIntegrationId = "987654" }));

        var result = await client.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                "sub_attempt",
                SubscriptionPaymentMethod.Card,
                299m,
                "EGP",
                Billing(),
                6755));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.NotConfigured", result.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Card_enrollment_carries_the_mapped_provider_plan_and_reports_capability()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan, 6755);
        var paymob = new FakePaymobClient();

        var result = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId,
                plan.Id,
                null,
                SubscriptionPaymentMethod.Card,
                Billing(),
                DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(6755, paymob.LastInput!.SubscriptionPlanId);
        Assert.True(result.Value.RecurringRenewalSupported);
    }

    [Fact]
    public async Task Paymob_card_enrollment_sends_documented_intention_fields_without_secret_in_body()
    {
        var handler = new RecordingHttpMessageHandler();
        var client = new PaymobClient(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                SecretKey = "sk_test_secret",
                PublicKey = "pk_test",
                Card3dsIntegrationId = "987654",
                WebhookUrl = "https://api.example.test/paymob/webhook",
                RedirectionUrl = "https://app.example.test/subscription/complete"
            }));

        var result = await client.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                "sub_attempt",
                SubscriptionPaymentMethod.Card,
                299m,
                "EGP",
                Billing(),
                6755));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("Token sk_test_secret", handler.Request!.Headers.Authorization!.ToString());
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        JsonElement root = body.RootElement;
        Assert.Equal(29900, root.GetProperty("amount").GetInt64());
        Assert.Equal("EGP", root.GetProperty("currency").GetString());
        Assert.Equal(987654, root.GetProperty("payment_methods")[0].GetInt64());
        Assert.Equal(6755, root.GetProperty("subscription_plan_id").GetInt32());
        Assert.Equal("sub_attempt", root.GetProperty("special_reference").GetString());
        Assert.Equal("https://api.example.test/paymob/webhook", root.GetProperty("notification_url").GetString());
        Assert.Equal("https://app.example.test/subscription/complete", root.GetProperty("redirection_url").GetString());
        Assert.Equal(29900, root.GetProperty("items")[0].GetProperty("amount").GetInt64());
        Assert.DoesNotContain("sk_test_secret", handler.Body!, StringComparison.Ordinal);
        Assert.True(result.Value.RecurringRenewalSupported);
    }

    [Fact]
    public async Task Paymob_card_enrollment_without_3ds_configuration_makes_no_provider_call()
    {
        var handler = new RecordingHttpMessageHandler();
        var client = new PaymobClient(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions { SecretKey = "sk_test_secret" }));

        var result = await client.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                "sub_attempt",
                SubscriptionPaymentMethod.Card,
                299m,
                "EGP",
                Billing(),
                6755));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.MethodNotAvailable", result.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Failed_and_pending_webhooks_do_not_activate_and_success_activates_once()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var intent = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);
        string reference = intent.Value.MerchantReference;
        long expectedAmountCents = (long)(intent.Value.Amount * 100m);

        var pending = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(reference, 10, expectedAmountCents, "EGP", false, true, DateTime.UtcNow), default);
        Assert.Equal("Pending", pending.Value.Outcome);
        Assert.Empty(db.FamilySubscriptions);

        var failed = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(reference, 11, expectedAmountCents, "EGP", false, false, DateTime.UtcNow), default);
        Assert.Equal("Failed", failed.Value.Outcome);
        Assert.Empty(db.FamilySubscriptions);

        var secondIntent = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow), default);
        long secondExpectedAmountCents = (long)(secondIntent.Value.Amount * 100m);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(secondIntent.Value.MerchantReference, 12, secondExpectedAmountCents, "EGP", true, false, DateTime.UtcNow), default);
        var duplicate = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(secondIntent.Value.MerchantReference, 13, secondExpectedAmountCents, "EGP", true, false, DateTime.UtcNow), default);

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
        var intent = await IntentHandler(db, paymob).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow), default);

        var amountMismatch = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(intent.Value.MerchantReference, 20, 1, "EGP", true, false, DateTime.UtcNow), default);
        var currencyMismatch = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
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

        var early = await RenewalIntentHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), created.AddDays(15)), default);
        Assert.False(early.IsSuccess);
        Assert.Equal("Subscriptions.Renewal.NotDue", early.Error.Code);

        var result = await RenewalIntentHandler(db, paymob).Handle(
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

        var first = await RenewalIntentHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), failureTime), default);
        var failed = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(first.Value.MerchantReference, 31, (long)(first.Value.Amount * 100m), "EGP", false, false, failureTime), default);

        Assert.Equal("Failed", failed.Value.Outcome);
        Assert.True(subscription.IsWithinRenewalGrace(periodEnd.AddDays(3)));

        var retry = await RenewalIntentHandler(db, paymob).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), periodEnd.AddDays(3)), default);
        long retryAmountCents = (long)(retry.Value.Amount * 100m);
        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(retry.Value.MerchantReference, 32, retryAmountCents, "EGP", true, false, periodEnd.AddDays(3)), default);

        Assert.Equal("Paid", paid.Value.Outcome);
        Assert.Equal(periodEnd.AddMonths(1), subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
    }

    // ---------------------- E04-B platform control gates ----------------------

    private static CreateSubscriptionPaymentIntentCommandHandler IntentHandler(
        FamiliesDbContext db,
        IPaymobClient paymob,
        FixedPlatformControls? controls = null)
    {
        // E04: one explicit in-memory control record supplies both the control
        // state and the method-configuration truth.
        FixedPlatformControls platform = controls ?? FixedPlatformControls.Operational();

        return new CreateSubscriptionPaymentIntentCommandHandler(
            db, paymob, new FixedPlatformChargeRules(10m, 5m), platform, platform);
    }

    private static CreateSubscriptionRenewalPaymentIntentCommandHandler RenewalIntentHandler(
        FamiliesDbContext db,
        IPaymobClient paymob,
        FixedPlatformControls? controls = null)
    {
        FixedPlatformControls platform = controls ?? FixedPlatformControls.Operational();

        return new CreateSubscriptionRenewalPaymentIntentCommandHandler(
            db, paymob, new FixedPlatformChargeRules(10m, 5m), platform, platform);
    }

    [Fact]
    public async Task Initial_intent_during_maintenance_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();

        var result = await IntentHandler(db, paymob, FixedPlatformControls.Maintenance()).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.Maintenance", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Initial_intent_for_a_disabled_card_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { CardEnabled = false };

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);

        // The still-configured and still-enabled Wallet method remains usable.
        Assert.True((await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Wallet, Billing(), DateTime.UtcNow),
            default)).IsSuccess);
    }

    [Fact]
    public async Task Initial_intent_for_an_unconfigured_wallet_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { WalletConfigured = false };

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Wallet, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Card_enrollment_intent_without_the_3ds_integration_is_rejected_before_any_provider_call()
    {
        // A plan that carries a provider subscription plan id sends this Card
        // intent through the dedicated 3DS enrollment integration, which is a
        // different integration id from the generic Card one, so the generic
        // Card configuration alone cannot satisfy it.
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan, 6755);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls
        {
            CardConfigured = true,
            CardSubscriptionEnrollmentConfigured = false
        };

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Null(paymob.LastInput);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Card_enrollment_intent_with_the_3ds_integration_keeps_the_existing_success_flow()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan, 6755);
        var paymob = new FakePaymobClient();

        var result = await IntentHandler(db, paymob, FixedPlatformControls.Operational()).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RecurringRenewalSupported);
        Assert.Equal(1, paymob.IntentCalls);
        Assert.Equal(6755, paymob.LastInput!.SubscriptionPlanId);

        var attempt = await db.SubscriptionPaymentAttempts.SingleAsync();
        Assert.Equal(SubscriptionPaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(result.Value.MerchantReference, attempt.MerchantReference);
    }

    [Fact]
    public async Task Card_intent_for_a_plan_without_a_provider_plan_id_keeps_the_generic_card_rule()
    {
        // No provider subscription plan id: no enrollment is sent, so the
        // generic Card integration remains sufficient.
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { CardSubscriptionEnrollmentConfigured = false };

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RecurringRenewalSupported);
        Assert.Equal(1, paymob.IntentCalls);
        Assert.Null(paymob.LastInput!.SubscriptionPlanId);
        Assert.Single(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Wallet_intent_for_an_enrollment_plan_is_unaffected_by_the_3ds_prerequisite()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan, 6755);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { CardSubscriptionEnrollmentConfigured = false };

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Wallet, Billing(), DateTime.UtcNow),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, paymob.IntentCalls);
        Assert.Null(paymob.LastInput!.SubscriptionPlanId);
        Assert.Single(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Renewal_intent_is_unaffected_by_the_3ds_enrollment_prerequisite()
    {
        // Renewal never sends an enrollment plan id, so it keeps the generic
        // Card integration rule even when the plan itself has a provider id.
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan, 6755);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1)));
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { CardSubscriptionEnrollmentConfigured = false };

        var result = await RenewalIntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), created.AddMonths(1)),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, paymob.IntentCalls);
        Assert.Null(paymob.LastInput!.SubscriptionPlanId);
        Assert.Single(db.SubscriptionPaymentAttempts);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public async Task Subscription_intent_fails_closed_when_the_control_record_is_unavailable(string state)
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();

        FixedPlatformControls controls = state == "missing"
            ? FixedPlatformControls.Missing()
            : FixedPlatformControls.Unreadable();

        var result = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Renewal_intent_during_maintenance_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1));
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();

        var result = await RenewalIntentHandler(db, paymob, FixedPlatformControls.Maintenance()).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Card, Billing(), created.AddMonths(1)),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.Maintenance", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
    }

    [Fact]
    public async Task Renewal_intent_for_an_unconfigured_wallet_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1)));
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls { WalletConfigured = false, WalletEnabled = true };

        var result = await RenewalIntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, SubscriptionPaymentMethod.Wallet, Billing(), created.AddMonths(1)),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(0, "Platform.PaymentMethodUnavailable")]
    [InlineData(99, "Platform.PaymentMethodUnavailable")]
    public async Task Subscription_gate_maps_only_defined_methods(int method, string? expectedCode)
    {
        // Exhaustive switch: an undefined enum value (an unvalidated cast that
        // reaches a direct handler call, e.g. renewal) must fail closed as
        // unavailable instead of falling through to Wallet.
        var controls = new FixedPlatformControls();

        Error? error = await PlatformControlGate.CheckNewPaymentIntentAsync(
            controls,
            controls,
            (SubscriptionPaymentMethod)method,
            CancellationToken.None);

        Assert.Equal(expectedCode, error?.Code);
    }

    [Fact]
    public async Task Undefined_method_on_renewal_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        DateTime created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1)));
        await db.SaveChangesAsync();
        var paymob = new FakePaymobClient();

        var result = await RenewalIntentHandler(db, paymob, new FixedPlatformControls()).Handle(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                family.OwnerUserId, (SubscriptionPaymentMethod)99, Billing(), created.AddMonths(1)),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Undefined_method_on_initial_intent_is_rejected_before_any_provider_call()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();

        var result = await IntentHandler(db, paymob, new FixedPlatformControls()).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, (SubscriptionPaymentMethod)99, Billing(), DateTime.UtcNow),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(db.SubscriptionPaymentAttempts);
    }

    [Fact]
    public async Task Existing_pending_attempt_still_settles_exactly_once_while_the_platform_is_in_maintenance()
    {
        await using var db = SeedDb(out Family family, out SubscriptionPlanVersion plan);
        var paymob = new FakePaymobClient();
        var controls = new FixedPlatformControls();

        var initial = await IntentHandler(db, paymob, controls).Handle(
            new CreateSubscriptionPaymentIntentCommand(
                family.OwnerUserId, plan.Id, null, SubscriptionPaymentMethod.Card, Billing(), DateTime.UtcNow),
            default);
        Assert.True(initial.IsSuccess);
        long amountCents = (long)(initial.Value.Amount * 100m);

        // The platform enters maintenance after the intent already exists: the
        // verified provider callback path never consults the control record and
        // must still settle the existing payment exactly once.
        controls.State = PlatformState.Maintenance;

        var paid = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(
                initial.Value.MerchantReference, 41, amountCents, "EGP", true, false, DateTime.UtcNow),
            default);
        Assert.Equal("Paid", paid.Value.Outcome);

        var duplicate = await new ConfirmSubscriptionPaymentCommandHandler(db, paymob).Handle(
            new ConfirmSubscriptionPaymentCommand(
                initial.Value.MerchantReference, 41, amountCents, "EGP", true, false, DateTime.UtcNow),
            default);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);
    }

    private static FamiliesDbContext SeedDb(
        out Family family,
        out SubscriptionPlanVersion plan,
        int? paymobSubscriptionPlanId = null)
    {        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DateTime now = DateTime.UtcNow;
        family = Family.Create(UserId.New());
        plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            true,
            true,
            now.AddDays(-1),
            now.AddDays(-1),
            paymobSubscriptionPlanId);
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

        public int IntentCalls { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.MethodNotAvailable", "Booking path not used.")));

        public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
            PaymobSubscriptionPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            IntentCalls++;
            LastInput = input;
            return Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.MerchantReference,
                    "sub-intention",
                    "sub-secret",
                    "pk_test",
                    input.Method == SubscriptionPaymentMethod.Card && input.SubscriptionPlanId is not null)));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success(null));
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    client_secret = "client_secret_value",
                    intention_order_id = 123456
                })
            };
        }
    }
}
