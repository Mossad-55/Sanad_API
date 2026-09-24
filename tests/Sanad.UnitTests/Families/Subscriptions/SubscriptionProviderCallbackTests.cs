using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Payments;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionProviderCallbackTests
{
    [Fact]
    public async Task Subscription_callback_uses_documented_hmac_formula_and_rejects_wrong_hmac()
    {
        const string secret = "test-secret";
        const string trigger = "Successful Transaction";
        const string providerSubscriptionId = "provider-sub-1";
        string expected = Convert.ToHexString(HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{trigger}for{providerSubscriptionId}"))).ToLowerInvariant();
        using JsonDocument document = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\"}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{expected}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid")));
        var controller = CreateController(sender, secret);

        IActionResult valid = await controller.HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<OkResult>(valid);
        Assert.IsType<HandlePaymobSubscriptionCallbackCommand>(sender.LastRequest);

        using JsonDocument invalidDocument = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\"}},\"trigger_type\":\"{trigger}\",\"hmac\":\"wrong\"}}");
        var invalidSender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid")));

        IActionResult invalid = await CreateController(invalidSender, secret)
            .HandlePaymob(null, invalidDocument.RootElement.Clone(), default);

        Assert.IsType<UnauthorizedResult>(invalid);
        Assert.Null(invalidSender.LastRequest);
    }

    [Fact]
    public async Task Subscription_callback_forwards_paymob_request_id_as_durable_identity()
    {
        const string secret = "test-secret";
        const string trigger = "Successful Transaction";
        const string providerSubscriptionId = "provider-sub-1";
        const string paymobRequestId = "paymob-request-42";
        using JsonDocument document = JsonDocument.Parse($"{{\"paymob_request_id\":\"{paymobRequestId}\",\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\",\"amount_cents\":29900}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{Hmac(secret, trigger, providerSubscriptionId)}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid")));

        IActionResult result = await CreateController(sender, secret)
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(paymobRequestId,
            Assert.IsType<HandlePaymobSubscriptionCallbackCommand>(sender.LastRequest).PaymobRequestId);

        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        subscription.AssociatePaymobSubscription(providerSubscriptionId, "active", null);
        await db.SaveChangesAsync();

        var callback = new HandlePaymobSubscriptionCallbackCommand(
            trigger, providerSubscriptionId, null, 29900, "active", null,
            subscription.CurrentPeriodEndsOnUtc.AddHours(1), paymobRequestId);
        var callbackResult = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(callback, default);

        Assert.Equal("Paid", callbackResult.Value.Outcome);
        PaymobSubscriptionCallback ledger = Assert.Single(db.PaymobSubscriptionCallbacks);
        Assert.Equal(paymobRequestId, ledger.PaymobRequestId);
        Assert.Equal($"request:{paymobRequestId}", ledger.CallbackKey);
    }

    [Theory]
    [InlineData("CREATED")]
    [InlineData("Subscription Created")]
    [InlineData("Successful Transaction")]
    [InlineData("Failed Transaction")]
    [InlineData("Failed Overdue Transaction")]
    public async Task Known_subscription_triggers_are_forwarded(string trigger)
    {
        const string secret = "test-secret";
        const string providerSubscriptionId = "provider-sub-1";
        string hmac = Hmac(secret, trigger, providerSubscriptionId);
        using JsonDocument document = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\"}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{hmac}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Ignored")));

        IActionResult result = await CreateController(sender, secret)
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(trigger, Assert.IsType<HandlePaymobSubscriptionCallbackCommand>(sender.LastRequest).TriggerType);
    }

    [Fact]
    public async Task Unknown_trigger_is_acknowledged_without_dispatch()
    {
        const string secret = "test-secret";
        const string trigger = "Provider Invented Event";
        const string providerSubscriptionId = "provider-sub-1";
        using JsonDocument document = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\"}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{Hmac(secret, trigger, providerSubscriptionId)}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid")));

        IActionResult result = await CreateController(sender, secret)
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<OkResult>(result);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Created_callback_requires_provider_identity_and_initial_transaction_correlation()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out SubscriptionPaymentAttempt attempt, succeededInitial: true);
        DateTime now = Utc(2026, 9, 24, 10);

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Subscription Created", "provider-sub-1", "initial-transaction", null,
                "active", now.AddMonths(1), now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Created", result.Value.Outcome);
        Assert.Equal("provider-sub-1", attempt.PaymobSubscriptionId);
        Assert.Equal("initial-transaction", attempt.PaymobInitialTransactionId);
        Assert.Equal("provider-sub-1", subscription.PaymobSubscriptionId);
        Assert.Equal("active", subscription.PaymobSubscriptionState);
        Assert.Equal(now.AddMonths(1), subscription.PaymobNextBillingOnUtc);
    }

    [Fact]
    public async Task Conflicting_created_provider_identity_is_rejected_without_mutation()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out SubscriptionPaymentAttempt attempt, succeededInitial: true);
        attempt.RecordPaymobSubscription("provider-sub-existing", "initial-transaction");
        subscription.AssociatePaymobSubscription("provider-sub-existing", "active", null);
        await db.SaveChangesAsync();
        Guid lifecycleVersion = subscription.LifecycleVersion;

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Subscription Created", "provider-sub-conflict", "initial-transaction", null,
                "active", Utc(2026, 10, 24, 10), Utc(2026, 9, 24, 10), "created-conflict"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Payment.ProviderIdentityConflict", result.Error.Code);
        Assert.Equal("provider-sub-existing", attempt.PaymobSubscriptionId);
        Assert.Equal("provider-sub-existing", subscription.PaymobSubscriptionId);
        Assert.Equal(lifecycleVersion, subscription.LifecycleVersion);
        Assert.Empty(db.PaymobSubscriptionCallbacks);
    }

    [Fact]
    public async Task Created_callback_rejects_provider_identity_already_owned_by_another_family()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription firstSubscription, out SubscriptionPaymentAttempt firstAttempt, succeededInitial: true);
        DateTime created = Utc(2026, 8, 24, 10);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, created, created);
        Family secondFamily = Family.Create(UserId.New());
        FamilySubscription secondSubscription = FamilySubscription.Create(
            secondFamily.Id, plan, created, created.AddMonths(1));
        SubscriptionPaymentAttempt secondAttempt = SubscriptionPaymentAttempt.Create(
            secondFamily.Id, plan, plan.Price, 0m, 0m, plan.Price, 0m, 0m, plan.Price,
            null, SubscriptionPaymentMethod.Card, created);
        secondAttempt.TryMarkSucceeded("initial-transaction-2", created.AddMinutes(1));
        secondAttempt.LinkSubscription(secondSubscription.Id);
        db.Families.Add(secondFamily);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(secondSubscription);
        db.SubscriptionPaymentAttempts.Add(secondAttempt);
        await db.SaveChangesAsync();

        const string providerSubscriptionId = "provider-sub-shared";
        var handler = new HandlePaymobSubscriptionCallbackCommandHandler(db);
        var first = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Subscription Created", providerSubscriptionId, "initial-transaction", null,
            "active", created.AddMonths(1), created, "created-first"), default);
        Guid secondLifecycleVersion = secondSubscription.LifecycleVersion;

        var second = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Subscription Created", providerSubscriptionId, "initial-transaction-2", null,
            "active", created.AddMonths(1), created, "created-second"), default);

        Assert.Equal("Created", first.Value.Outcome);
        Assert.True(second.IsFailure);
        Assert.Equal("Subscriptions.Payment.ProviderIdentityConflict", second.Error.Code);
        Assert.Equal(providerSubscriptionId, firstAttempt.PaymobSubscriptionId);
        Assert.Equal(providerSubscriptionId, firstSubscription.PaymobSubscriptionId);
        Assert.Null(secondAttempt.PaymobSubscriptionId);
        Assert.Null(secondSubscription.PaymobSubscriptionId);
        Assert.Equal(secondLifecycleVersion, secondSubscription.LifecycleVersion);
        PaymobSubscriptionIdentity identity = Assert.Single(db.PaymobSubscriptionIdentities);
        Assert.Equal(providerSubscriptionId, identity.ProviderSubscriptionId);
        Assert.Equal(firstAttempt.Id, identity.PaymentAttemptId);
        Assert.Equal(firstSubscription.Id, identity.FamilySubscriptionId);
        PaymobSubscriptionCallback ledger = Assert.Single(db.PaymobSubscriptionCallbacks);
        Assert.Equal("request:created-first", ledger.CallbackKey);
    }

    [Fact]
    public async Task Same_target_created_callback_with_same_provider_identity_is_idempotent()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out SubscriptionPaymentAttempt attempt, succeededInitial: true);
        DateTime now = Utc(2026, 9, 24, 10);
        var request = new HandlePaymobSubscriptionCallbackCommand(
            "Subscription Created", "provider-sub-created", "initial-transaction", null,
            "active", now.AddMonths(1), now, "created-replay");
        var handler = new HandlePaymobSubscriptionCallbackCommandHandler(db);

        var first = await handler.Handle(request, default);
        Guid lifecycleVersion = subscription.LifecycleVersion;
        DateTime? nextBillingOnUtc = subscription.PaymobNextBillingOnUtc;
        var duplicate = await handler.Handle(request, default);

        Assert.Equal("Created", first.Value.Outcome);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);
        Assert.Equal("provider-sub-created", attempt.PaymobSubscriptionId);
        Assert.Equal("provider-sub-created", subscription.PaymobSubscriptionId);
        Assert.Equal(lifecycleVersion, subscription.LifecycleVersion);
        Assert.Equal(nextBillingOnUtc, subscription.PaymobNextBillingOnUtc);
        PaymobSubscriptionIdentity identity = Assert.Single(db.PaymobSubscriptionIdentities);
        Assert.Equal("provider-sub-created", identity.ProviderSubscriptionId);
        Assert.Equal(attempt.Id, identity.PaymentAttemptId);
        Assert.Equal(subscription.Id, identity.FamilySubscriptionId);
        Assert.Single(db.PaymobSubscriptionCallbacks);
    }

    [Fact]
    public void Provider_subscription_identity_registry_has_shared_unique_ownership_boundary()
    {
        using FamiliesDbContext db = CreateContext(Guid.NewGuid().ToString());

        var subscriptionIndex = Assert.Single(
            db.Model.FindEntityType(typeof(FamilySubscription))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_family_subscriptions_paymob_subscription_id");
        var attemptIndex = Assert.Single(
            db.Model.FindEntityType(typeof(SubscriptionPaymentAttempt))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_subscription_payment_attempts_paymob_subscription_id");

        Assert.True(subscriptionIndex.IsUnique);
        Assert.Equal("paymob_subscription_id IS NOT NULL", subscriptionIndex.GetFilter());
        Assert.True(attemptIndex.IsUnique);
        Assert.Equal("paymob_subscription_id IS NOT NULL", attemptIndex.GetFilter());

        var registryType = db.Model.FindEntityType(typeof(PaymobSubscriptionIdentity))!;
        Assert.Equal("paymob_subscription_identities", registryType.GetTableName());
        Assert.Equal("families", registryType.GetSchema());
        var registryIndex = Assert.Single(
            registryType.GetIndexes(),
            index => index.GetDatabaseName() == "ux_paymob_subscription_identities_provider_subscription_id");
        Assert.True(registryIndex.IsUnique);
        Assert.Null(registryIndex.GetFilter());
        Assert.Equal(100, registryType.FindProperty(nameof(PaymobSubscriptionIdentity.ProviderSubscriptionId))!.GetMaxLength());

        Assert.Contains(
            registryType.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Single().Name == nameof(PaymobSubscriptionIdentity.PaymentAttemptId)
                && foreignKey.PrincipalEntityType.ClrType == typeof(SubscriptionPaymentAttempt)
                && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            registryType.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Single().Name == nameof(PaymobSubscriptionIdentity.FamilySubscriptionId)
                && foreignKey.PrincipalEntityType.ClrType == typeof(FamilySubscription)
                && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public async Task Created_callback_maps_shared_registry_duplicate_race_to_cross_target_conflict()
    {
        await using FamiliesDbContext inner = CreateDb(out _, out _, succeededInitial: true);
        var context = new FailingSaveContext(inner,
            new DbUpdateException("23505: duplicate key violates unique constraint "
                + "ux_paymob_subscription_identities_provider_subscription_id"));

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(context).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Subscription Created", "provider-sub-race", "initial-transaction", null,
                "active", Utc(2026, 10, 24, 10), Utc(2026, 9, 24, 10), "created-race"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Payment.ProviderIdentityConflict", result.Error.Code);
    }

    [Fact]
    public async Task Successful_renewal_advances_original_anchor_once_and_duplicate_is_idempotent()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        DateTime settlement = periodEnd.AddHours(1);
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();

        var handler = new HandlePaymobSubscriptionCallbackCommandHandler(db);
        var request = new HandlePaymobSubscriptionCallbackCommand(
            "Successful Transaction", "provider-sub-1", null, 29900, "active", null, settlement,
            "renewal-request-1");
        var first = await handler.Handle(request, default);
        DateTime expectedAnchor = periodEnd.AddMonths(1);

        var duplicate = await handler.Handle(request, default);

        Assert.Equal("Paid", first.Value.Outcome);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);
        Assert.Equal(expectedAnchor, subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
        PaymobSubscriptionCallback ledger = Assert.Single(db.PaymobSubscriptionCallbacks);
        Assert.Equal("renewal-request-1", ledger.PaymobRequestId);
        Assert.Equal("request:renewal-request-1", ledger.CallbackKey);
    }

    [Fact]
    public async Task Renewal_without_paymob_request_id_fails_without_mutating_subscription_or_ledger()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();

        DateTime? grace = subscription.RenewalGraceEndsOnUtc;
        DateTime? lastFailure = subscription.LastRenewalFailedOnUtc;
        string? callbackKey = subscription.PaymobLastCallbackKey;

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Successful Transaction", "provider-sub-1", null, 29900, "active", null,
                periodEnd.AddHours(1)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Callback.ProviderEventIdRequired", result.Error.Code);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
        Assert.Equal(grace, subscription.RenewalGraceEndsOnUtc);
        Assert.Equal(lastFailure, subscription.LastRenewalFailedOnUtc);
        Assert.Equal(callbackKey, subscription.PaymobLastCallbackKey);
        Assert.Empty(db.PaymobSubscriptionCallbacks);
        Assert.Empty(db.PaymobSubscriptionIdentities);
    }

    [Fact]
    public async Task Duplicate_paymob_request_id_is_idempotent_even_when_payload_differs()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();
        const string requestId = "paymob-request-duplicate";
        var handler = new HandlePaymobSubscriptionCallbackCommandHandler(db);

        var first = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Successful Transaction", "provider-sub-1", null, 29900, "active", null,
            periodEnd.AddHours(1), requestId), default);
        DateTime anchor = subscription.CurrentPeriodEndsOnUtc;
        DateTime? grace = subscription.RenewalGraceEndsOnUtc;

        var duplicate = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Failed Transaction", "provider-sub-1", null, 29900, "active", null,
            periodEnd.AddHours(2), requestId), default);

        Assert.Equal("Paid", first.Value.Outcome);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);
        Assert.Equal(anchor, subscription.CurrentPeriodEndsOnUtc);
        Assert.Equal(grace, subscription.RenewalGraceEndsOnUtc);
        Assert.Single(db.PaymobSubscriptionCallbacks);
        Assert.Equal(requestId, db.PaymobSubscriptionCallbacks.Single().PaymobRequestId);
        Assert.Equal($"request:{requestId}", db.PaymobSubscriptionCallbacks.Single().CallbackKey);
    }

    [Fact]
    public async Task Older_valid_callback_after_newer_callback_does_not_mutate_subscription_state()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();
        var handler = new HandlePaymobSubscriptionCallbackCommandHandler(db);

        var newer = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Successful Transaction", "provider-sub-1", null, 29900, "active", null,
            periodEnd.AddHours(2), "paymob-request-newer"), default);
        DateTime anchor = subscription.CurrentPeriodEndsOnUtc;
        DateTime? grace = subscription.RenewalGraceEndsOnUtc;
        DateTime? lastFailure = subscription.LastRenewalFailedOnUtc;
        string? lastCallbackKey = subscription.PaymobLastCallbackKey;

        var older = await handler.Handle(new HandlePaymobSubscriptionCallbackCommand(
            "Failed Transaction", "provider-sub-1", null, 29900, "active", null,
            periodEnd.AddHours(1), "paymob-request-older"), default);

        Assert.Equal("Paid", newer.Value.Outcome);
        Assert.Equal("AlreadyProcessed", older.Value.Outcome);
        Assert.Equal(anchor, subscription.CurrentPeriodEndsOnUtc);
        Assert.Equal(grace, subscription.RenewalGraceEndsOnUtc);
        Assert.Equal(lastFailure, subscription.LastRenewalFailedOnUtc);
        Assert.Equal(lastCallbackKey, subscription.PaymobLastCallbackKey);
    }

    [Fact]
    public async Task Failed_transaction_starts_exactly_seven_day_grace_without_extending_access()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();
        DateTime failedOn = periodEnd.AddMinutes(1);

        var result = await Callback(db, "Failed Transaction", failedOn);

        Assert.Equal("Failed", result.Value.Outcome);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
        Assert.Equal(periodEnd.AddDays(7), subscription.RenewalGraceEndsOnUtc);
        Assert.True(subscription.IsWithinRenewalGrace(periodEnd.AddDays(7).AddTicks(-1)));
        Assert.False(subscription.IsWithinRenewalGrace(periodEnd.AddDays(7)));
    }

    [Fact]
    public async Task Failed_overdue_transaction_does_not_extend_access_or_start_grace()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();

        var result = await Callback(db, "Failed Overdue Transaction", periodEnd.AddDays(2));

        Assert.Equal("Overdue", result.Value.Outcome);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.RenewalGraceEndsOnUtc);
    }

    [Fact]
    public async Task Failed_overdue_transaction_does_not_reset_or_extend_existing_grace()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();
        var failed = await Callback(db, "Failed Transaction", periodEnd.AddMinutes(1));
        Assert.Equal("Failed", failed.Value.Outcome);
        DateTime grace = subscription.RenewalGraceEndsOnUtc!.Value;
        DateTime failedOn = subscription.LastRenewalFailedOnUtc!.Value;

        var overdue = await Callback(db, "Failed Overdue Transaction", periodEnd.AddDays(2));

        Assert.Equal("Overdue", overdue.Value.Outcome);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
        Assert.Equal(grace, subscription.RenewalGraceEndsOnUtc);
        Assert.Equal(failedOn, subscription.LastRenewalFailedOnUtc);
    }

    [Fact]
    public async Task Grace_expired_successful_renewal_persists_one_ledger_callback_without_mutating_renewal_state()
    {
        string database = Guid.NewGuid().ToString();
        DateTime callbackTime;
        DateTime periodEnd;
        DateTime graceEnds;
        DateTime failedOn;
        string? callbackKey;
        string? providerState;

        await using (FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false, database))
        {
            periodEnd = subscription.CurrentPeriodEndsOnUtc;
            subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
            subscription.BeginRenewalGrace(periodEnd.AddMinutes(1));
            await db.SaveChangesAsync();

            graceEnds = subscription.RenewalGraceEndsOnUtc!.Value;
            failedOn = subscription.LastRenewalFailedOnUtc!.Value;
            callbackKey = subscription.PaymobLastCallbackKey;
            providerState = subscription.PaymobSubscriptionState;
            callbackTime = graceEnds.AddTicks(1);

            var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
                new HandlePaymobSubscriptionCallbackCommand(
                    "Successful Transaction", "provider-sub-1", null, 29900, "expired-state",
                    periodEnd.AddMonths(1), callbackTime, "grace-expired-request"), default);

            Assert.True(result.IsFailure);
            Assert.Equal("Subscriptions.Renewal.GraceExpired", result.Error.Code);
            Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
            Assert.Equal(graceEnds, subscription.RenewalGraceEndsOnUtc);
            Assert.Equal(failedOn, subscription.LastRenewalFailedOnUtc);
            Assert.Equal(callbackKey, subscription.PaymobLastCallbackKey);
            Assert.Equal(providerState, subscription.PaymobSubscriptionState);
            PaymobSubscriptionCallback ledger = Assert.Single(db.PaymobSubscriptionCallbacks);
            Assert.Equal("grace-expired-request", ledger.PaymobRequestId);
            Assert.Equal("request:grace-expired-request", ledger.CallbackKey);
        }

        await using FamiliesDbContext persistedDb = CreateContext(database);
        FamilySubscription persisted = await persistedDb.FamilySubscriptions.SingleAsync();
        Assert.Equal(periodEnd, persisted.CurrentPeriodEndsOnUtc);
        Assert.Equal(graceEnds, persisted.RenewalGraceEndsOnUtc);
        Assert.Equal(failedOn, persisted.LastRenewalFailedOnUtc);
        Assert.Equal(callbackKey, persisted.PaymobLastCallbackKey);
        Assert.Equal(providerState, persisted.PaymobSubscriptionState);
        PaymobSubscriptionCallback persistedLedger = Assert.Single(persistedDb.PaymobSubscriptionCallbacks);
        Assert.Equal("grace-expired-request", persistedLedger.PaymobRequestId);
        Assert.Equal("request:grace-expired-request", persistedLedger.CallbackKey);
    }

    [Theory]
    [InlineData("{\"subscription_data\":{},\"trigger_type\":\"Successful Transaction\",\"hmac\":\"x\"}", 400)]
    [InlineData("{\"subscription_data\":{\"id\":\"provider-sub-1\"},\"hmac\":\"x\"}", 400)]
    [InlineData("{\"subscription_data\":{\"id\":\"provider-sub-1\"},\"trigger_type\":\"Successful Transaction\"}", 401)]
    public async Task Malformed_subscription_callback_shape_returns_safe_boundary_status(string payload, int expectedStatus)
    {
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Ignored")));
        using JsonDocument document = JsonDocument.Parse(payload);

        IActionResult result = await CreateController(sender, "test-secret")
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Missing_required_subscription_amount_is_rejected_by_application_boundary()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
        await db.SaveChangesAsync();

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Successful Transaction", "provider-sub-1", null, null, "active", null,
                subscription.CurrentPeriodEndsOnUtc.AddHours(1), "renewal-missing-amount"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.AmountMismatch", result.Error.Code);
        Assert.Empty(db.PaymobSubscriptionCallbacks);
    }

    [Fact]
    public async Task Controller_maps_callback_concurrency_conflict_to_conflict()
    {
        const string secret = "test-secret";
        const string trigger = "Successful Transaction";
        const string providerSubscriptionId = "provider-sub-1";
        using JsonDocument document = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\",\"amount_cents\":29900}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{Hmac(secret, trigger, providerSubscriptionId)}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Failure(
            new Error("Subscriptions.Callback.ConcurrencyConflict", "conflict")));

        IActionResult result = await CreateController(sender, secret)
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<ConflictResult>(result);
    }

    [Fact]
    public async Task Controller_acknowledges_grace_expired_callback_without_retry()
    {
        const string secret = "test-secret";
        const string trigger = "Failed Overdue Transaction";
        const string providerSubscriptionId = "provider-sub-1";
        using JsonDocument document = JsonDocument.Parse($"{{\"subscription_data\":{{\"id\":\"{providerSubscriptionId}\",\"amount_cents\":29900}},\"trigger_type\":\"{trigger}\",\"hmac\":\"{Hmac(secret, trigger, providerSubscriptionId)}\"}}");
        var sender = new RecordingSender(Result<ConfirmSubscriptionPaymentResponse>.Failure(
            new Error("Subscriptions.Renewal.GraceExpired", "grace expired")));

        IActionResult result = await CreateController(sender, secret)
            .HandlePaymob(null, document.RootElement.Clone(), default);

        Assert.IsType<OkResult>(result);
        Assert.IsType<HandlePaymobSubscriptionCallbackCommand>(sender.LastRequest);
    }

    [Fact]
    public async Task Callback_ledger_is_persisted_and_append_only()
    {
        string database = Guid.NewGuid().ToString();
        await using (FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false, database))
        {
            subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
            await db.SaveChangesAsync();
            var result = await Callback(db, "Failed Transaction", subscription.CurrentPeriodEndsOnUtc.AddMinutes(1));
            Assert.Equal("Failed", result.Value.Outcome);
        }

        await using FamiliesDbContext persistedDb = CreateContext(database);
        PaymobSubscriptionCallback ledger = Assert.Single(persistedDb.PaymobSubscriptionCallbacks);
        Assert.Equal("provider-sub-1", ledger.ProviderSubscriptionId);
        Assert.Equal("Failed Transaction", ledger.TriggerType);
        Guid ledgerId = ledger.Id;
        persistedDb.Entry(ledger).Property(item => item.TriggerType).CurrentValue = "Tampered";

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => persistedDb.SaveChangesAsync());

        Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ledgerId, ledger.Id);
    }

    [Fact]
    public async Task Unknown_provider_subscription_is_ignored_without_mutation()
    {
        await using FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;
        DateTime callbackTime = periodEnd.AddHours(1);

        var result = await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                "Successful Transaction", "unknown-provider", null, 29900, "active", null, callbackTime,
                "unknown-provider-event"), default);

        Assert.Equal("Ignored", result.Value.Outcome);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndsOnUtc);
        Assert.Null(subscription.PaymobSubscriptionId);
        Assert.Null(subscription.PaymobLastCallbackKey);
    }

    [Fact]
    public async Task Callback_state_persists_and_replayed_callback_does_not_mutate_it()
    {
        string database = Guid.NewGuid().ToString();
        await using (FamiliesDbContext db = CreateDb(out FamilySubscription subscription, out _, succeededInitial: false, database))
        {
            subscription.AssociatePaymobSubscription("provider-sub-1", "active", null);
            await db.SaveChangesAsync();
            var result = await Callback(db, "Failed Transaction", subscription.CurrentPeriodEndsOnUtc.AddMinutes(1));
            Assert.Equal("Failed", result.Value.Outcome);
        }

        await using FamiliesDbContext persistedDb = CreateContext(database);
        FamilySubscription persisted = await persistedDb.FamilySubscriptions.SingleAsync();
        DateTime anchor = persisted.CurrentPeriodEndsOnUtc;
        DateTime grace = persisted.RenewalGraceEndsOnUtc!.Value;
        var replay = await Callback(persistedDb, "Failed Transaction", persisted.LastRenewalFailedOnUtc!.Value);

        Assert.Equal("AlreadyProcessed", replay.Value.Outcome);
        Assert.Equal(anchor, persisted.CurrentPeriodEndsOnUtc);
        Assert.Equal(grace, persisted.RenewalGraceEndsOnUtc);
    }

    private static async Task<Result<ConfirmSubscriptionPaymentResponse>> Callback(
        FamiliesDbContext db,
        string trigger,
        DateTime utcNow,
        string? paymobRequestId = null) =>
        await new HandlePaymobSubscriptionCallbackCommandHandler(db).Handle(
            new HandlePaymobSubscriptionCallbackCommand(
                trigger, "provider-sub-1", null, 29900, "active", null, utcNow,
                paymobRequestId ?? $"renewal-{trigger}-{utcNow:O}"), default);

    private static FamiliesDbContext CreateDb(
        out FamilySubscription subscription,
        out SubscriptionPaymentAttempt attempt,
        bool succeededInitial,
        string? database = null)
    {
        FamiliesDbContext db = CreateContext(database ?? Guid.NewGuid().ToString());
        DateTime created = Utc(2026, 8, 24, 10);
        Family family = Family.Create(UserId.New());
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium, true, true, created, created);
        subscription = FamilySubscription.Create(family.Id, plan, created, created.AddMonths(1));
        attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, plan.Price, 0m, 0m, plan.Price, 0m, 0m, plan.Price,
            null, SubscriptionPaymentMethod.Card, created);
        if (succeededInitial)
            attempt.TryMarkSucceeded("initial-transaction", created.AddMinutes(1));
        attempt.LinkSubscription(subscription.Id);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        db.SubscriptionPaymentAttempts.Add(attempt);
        db.SaveChanges();
        return db;
    }

    private static FamiliesDbContext CreateContext(string database) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(database)
            .Options);

    private static PaymobWebhookController CreateController(RecordingSender sender, string secret) =>
        new(sender, Options.Create(new PaymobOptions { HmacSecret = secret }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static string Hmac(string secret, string trigger, string providerSubscriptionId) =>
        Convert.ToHexString(HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{trigger}for{providerSubscriptionId}"))).ToLowerInvariant();

    private static DateTime Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class FailingSaveContext(FamiliesDbContext inner, Exception failure) : IFamiliesDbContext
    {
        public DbSet<Family> Families => throw new NotSupportedException();
        public DbSet<Elderly> Elderlies => throw new NotSupportedException();
        public DbSet<FamilyInvitation> Invitations => throw new NotSupportedException();
        public DbSet<Booking> Bookings => throw new NotSupportedException();
        public DbSet<BookingCancellationFact> BookingCancellationFacts => throw new NotSupportedException();
        public DbSet<AssessmentQuestion> AssessmentQuestions => throw new NotSupportedException();
        public DbSet<AssessmentTier> AssessmentTiers => throw new NotSupportedException();
        public DbSet<CareAssessment> CareAssessments => throw new NotSupportedException();
        public DbSet<Medication> Medications => throw new NotSupportedException();
        public DbSet<MedicationDoseLog> MedicationDoseLogs => throw new NotSupportedException();
        public DbSet<ElderlyNote> ElderlyNotes => throw new NotSupportedException();
        public DbSet<ElderlyActivityLog> ElderlyActivityLogs => throw new NotSupportedException();
        public DbSet<VisitReport> VisitReports => throw new NotSupportedException();
        public DbSet<MedicalReport> MedicalReports => throw new NotSupportedException();
        public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => inner.SubscriptionPlanVersions;
        public DbSet<FamilySubscription> FamilySubscriptions => inner.FamilySubscriptions;
        public DbSet<SubscriptionPlanRetirementAudit> SubscriptionPlanRetirementAudits => throw new NotSupportedException();
        public DbSet<SubscriptionCoupon> SubscriptionCoupons => throw new NotSupportedException();
        public DbSet<SubscriptionTaxRule> SubscriptionTaxRules => throw new NotSupportedException();
        public DbSet<SubscriptionPaymentAttempt> SubscriptionPaymentAttempts => inner.SubscriptionPaymentAttempts;
        public DbSet<PaymobSubscriptionCallback> PaymobSubscriptionCallbacks => inner.PaymobSubscriptionCallbacks;
        public DbSet<PaymobSubscriptionIdentity> PaymobSubscriptionIdentities => inner.PaymobSubscriptionIdentities;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int>(failure);
    }

    private sealed class RecordingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
