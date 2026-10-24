using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.UnitTests.Families.Subscriptions;

/// <summary>
/// W3-F3 clause-9 subscription money outcomes on the Families side (duplicate
/// charge, service-shutdown proration, cancel-renewal recording).
/// <para>
/// M5: the shutdown proration base is the consideration settled for the CURRENT
/// paid period (the subscription's own <c>CurrentPeriodGross</c> snapshot), never
/// an all-time sum of captures and never the plan's list price. F3 exception:
/// a paid plan-change capture is measured by its incremental <c>TotalPayable</c>
/// (new money only), because the target gross in <c>CurrentPeriodGross</c>
/// includes the prorated credit that the SOURCE capture's budget already covers.
/// Counting it here too would make the same credit refundable twice across the
/// two capture budgets. The cost is a deliberate under-refund of the credited
/// share on the shutdown path, never a double refund.
/// </para>
/// <para>
/// M4: the refund ceiling is the CAPTURE. The duplicate charge names the attempt
/// it is correcting; the shutdown names the succeeded attempt that settled inside
/// the current period window and that period's consideration as its ceiling. An
/// older period's consumed money therefore supplies no headroom, and a period
/// whose capture cannot be resolved fails closed instead of falling back to a
/// lifetime total. The recorder seam is faked so these tests are provider- and
/// database-free; the cumulative claim itself lives in Finance and is pinned by
/// <c>SubscriptionRefundRecorderTests</c>.
/// </para>
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class SubscriptionRefundCommandsTests
{
    private static DateTime Utc(int day, int hour = 0) =>
        new(2026, 10, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Duplicate_charge_is_refunded_in_full()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, recorder);

        var result = await handler.Handle(new(owner, attempt.Id, "dup-key"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionMoneyOutcome.DuplicateCharge, result.Value.Outcome);
        Assert.Equal(34086L, result.Value.AwardedMinorUnits);
        Assert.Equal(SubscriptionRefundReason.DuplicateCharge, recorder.LastReason);
        Assert.Equal(34086L, recorder.LastAwarded.MinorUnits);
        // M4: the award is claimed against the DUPLICATED CAPTURE itself, whose
        // ceiling is that attempt's own paid amount — never an all-time total.
        Assert.Equal(subscription.Id, recorder.LastSubscriptionId);
        Assert.Equal(attempt.Id, recorder.LastPaymentAttemptId);
        Assert.Equal(34086L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task A_duplicate_charge_is_not_diluted_by_money_from_another_period()
    {
        // An older period captured 500 and the corrected attempt captured 340.86.
        // A lifetime-seeded ceiling would report 840.86; the capture ceiling must
        // report only what this attempt paid.
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));

        SubscriptionPaymentAttempt earlier = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 500m, 0m, 0m, 500m, 14m, 70m, 570m,
            null, SubscriptionPaymentMethod.Card, Utc(1).AddMonths(-1));
        earlier.LinkSubscription(subscription.Id);
        earlier.TryMarkSucceeded("txn-old", Utc(1).AddMonths(-1));

        SubscriptionPaymentAttempt corrected = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        corrected.LinkSubscription(subscription.Id);
        corrected.TryMarkSucceeded("txn-1", Utc(1));

        db.SubscriptionPaymentAttempts.AddRange(earlier, corrected);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, recorder);

        var result = await handler.Handle(new(owner, corrected.Id, "dup-key"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(corrected.Id, recorder.LastPaymentAttemptId);
        Assert.Equal(34086L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task A_non_succeeded_attempt_has_nothing_to_refund()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(new(owner, attempt.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NothingToRefund", result.Error.Code);
    }

    [Fact]
    public async Task A_non_owner_cannot_refund_a_duplicate_charge()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(new(editor, attempt.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NotOwner", result.Error.Code);
    }

    [Fact]
    public async Task Shutdown_prorates_the_unused_half_of_the_paid_period()
    {
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 300m));
        SubscriptionPaymentAttempt capture = await db.SubscriptionPaymentAttempts.SingleAsync();

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        // Shutdown at the exact midpoint of a 31-day period: unused ratio 0.5 of
        // the 300.00 settled for the current period = 150.00.
        DateTime midpoint = Utc(1).AddTicks((Utc(1).AddMonths(1) - Utc(1)).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionMoneyOutcome.ServiceShutdown, result.Value.Outcome);
        Assert.Equal(15000L, result.Value.AwardedMinorUnits);
        Assert.Equal(SubscriptionRefundReason.ServiceShutdown, recorder.LastReason);
        Assert.Equal(subscription.Id, recorder.LastSubscriptionId);
        // M4: the award is claimed against the capture that paid for this period,
        // with that period's settled consideration as the ceiling.
        Assert.Equal(capture.Id, recorder.LastPaymentAttemptId);
        Assert.Equal(30_000L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task Shutdown_at_or_after_the_period_end_is_exactly_zero()
    {
        await using FamiliesDbContext db = CreateContext();
        DateTime periodEnd = Utc(1).AddMonths(1);
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), periodEnd, (Utc(1), 300m));

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        var atEnd = await handler.Handle(new(subscription.Id, UserId.New(), periodEnd), default);
        var afterEnd = await handler.Handle(new(subscription.Id, UserId.New(), periodEnd.AddDays(5)), default);

        Assert.True(atEnd.IsSuccess);
        Assert.Equal(0L, atEnd.Value.AwardedMinorUnits);
        Assert.True(afterEnd.IsSuccess);
        Assert.Equal(0L, afterEnd.Value.AwardedMinorUnits);
    }

    [Fact]
    public async Task Shutdown_one_second_before_the_period_end_is_effectively_zero()
    {
        await using FamiliesDbContext db = CreateContext();
        DateTime periodEnd = Utc(1).AddMonths(1);
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), periodEnd, (Utc(1), 300m));

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        // #31: one second before the end the unused ratio rounds to 0 piastres on
        // a 300.00 period; the zero award is recorded as computed, never rounded up.
        var result = await handler.Handle(new(subscription.Id, UserId.New(), periodEnd.AddSeconds(-1)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0L, result.Value.AwardedMinorUnits);
    }

    [Fact]
    public async Task Shutdown_base_is_the_current_period_consideration_not_an_all_time_sum()
    {
        await using FamiliesDbContext db = CreateContext();
        // An older period captured 200, and the current period's capture was
        // 300. The base is the current period's 300, not the 500 lifetime sum.
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1),
            (Utc(1).AddMonths(-1), 200m),
            (Utc(1), 300m));
        SubscriptionPaymentAttempt current = await db.SubscriptionPaymentAttempts
            .SingleAsync(item => item.SettledOnUtc == Utc(1));

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        DateTime midpoint = Utc(1).AddTicks((Utc(1).AddMonths(1) - Utc(1)).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsSuccess);
        // Unused half of the CURRENT period's 300.00 = 150.00 (a lifetime sum
        // would have paid 250.00).
        Assert.Equal(15_000L, result.Value.AwardedMinorUnits);
        // The ceiling is that one capture, never the two periods combined.
        Assert.Equal(current.Id, recorder.LastPaymentAttemptId);
        Assert.Equal(30_000L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task Twelve_renewals_mid_current_period_pay_only_the_unused_current_period()
    {
        await using FamiliesDbContext db = CreateContext();
        DateTime periodStart = Utc(1);
        DateTime periodEnd = periodStart.AddMonths(1);

        // Twelve settled renewals from earlier periods (3,600.00) plus the 300.00
        // capture that established the current period. Only the current period is
        // at risk, so a shutdown at the midpoint refunds 150.00 — not the ~1,950.00
        // an all-time sum would produce — and only that capture is drawn on.
        var captures = new List<(DateTime, decimal)>();
        for (int monthsAgo = 12; monthsAgo >= 1; monthsAgo--)
            captures.Add((periodStart.AddMonths(-monthsAgo), 300m));
        captures.Add((periodStart, 300m));

        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, periodStart.AddMonths(-12), periodEnd, captures.ToArray());
        SubscriptionPaymentAttempt current = await db.SubscriptionPaymentAttempts
            .SingleAsync(item => item.SettledOnUtc == periodStart);

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        DateTime midpoint = periodStart.AddTicks((periodEnd - periodStart).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(15_000L, result.Value.AwardedMinorUnits);
        Assert.Equal(current.Id, recorder.LastPaymentAttemptId);
        // The ceiling is the current capture, not the 3,900.00 lifetime total.
        Assert.Equal(30_000L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task Shutdown_base_uses_only_the_plan_change_incremental_charge_to_avoid_refunding_credit_twice()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        DateTime createdOnUtc = Utc(1);
        DateTime periodEndsOnUtc = createdOnUtc.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            family.Id, plan, createdOnUtc, periodEndsOnUtc);
        db.FamilySubscriptions.Add(subscription);

        // Initial capture: 342.00 payable for the period.
        SubscriptionPaymentAttempt initial = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 300m, 0m, 0m, 300m, 14m, 42m, 342m,
            null, SubscriptionPaymentMethod.Card, createdOnUtc);
        initial.LinkSubscription(subscription.Id);
        initial.TryMarkSucceeded("txn-initial", createdOnUtc);
        db.SubscriptionPaymentAttempts.Add(initial);
        subscription.SetCurrentPeriodChargeSnapshot(initial);

        // Paid upgrade mid-period: 100.00 of the old period stays unused and is
        // credited, the target period gross is 400.00, so only 300.00 is charged
        // incrementally. The subscription records the TARGET gross, not the sum,
        // but the plan-change CAPTURE may claim only the 300.00 of new money: the
        // 100.00 credit is already covered by the initial capture's budget (F3).
        SubscriptionPaymentAttempt upgrade = SubscriptionPaymentAttempt.CreatePlanChange(
            subscription, plan,
            targetRemainingGross: 400m,
            sourcePeriodGross: 342m,
            proratedCredit: 100m,
            targetTaxRatePercentage: 14m,
            targetTaxAmount: 56m,
            SubscriptionPaymentMethod.Card,
            createdOnUtc.AddDays(15));
        upgrade.TryMarkSucceeded("txn-upgrade", createdOnUtc.AddDays(15));
        db.SubscriptionPaymentAttempts.Add(upgrade);
        subscription.ApplyPaidPlanChange(plan, upgrade);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        DateTime midpoint = createdOnUtc.AddTicks((periodEndsOnUtc - createdOnUtc).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsSuccess);
        // F3: the plan-change capture's consideration is the incremental 300.00,
        // not the 400.00 target gross, because the 100.00 credit is already
        // refundable against the initial capture. The unused half is therefore
        // 150.00. Using the target gross would refund 200.00 here and let the same
        // 100.00 credit be refunded once per budget; summing the charges
        // (342 + 300 = 642) would refund 321.00 and count even more.
        Assert.Equal(15_000L, result.Value.AwardedMinorUnits);
        // The plan-change capture owns the new-money ceiling: 300.00, not the
        // target gross and not the initial capture.
        Assert.Equal(upgrade.Id, recorder.LastPaymentAttemptId);
        Assert.Equal(30_000L, recorder.LastCapturedMinorUnits);
    }

    [Fact]
    public async Task A_shutdown_whose_period_capture_cannot_be_resolved_fails_closed()
    {
        // The only settlement on record falls OUTSIDE the current period window, so
        // the capture that paid for this period cannot be resolved. The path must
        // fail closed: no lifetime total, no arbitrary "latest attempt".
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1).AddDays(-10), 300m));

        var recorder = new FakeRecorder();
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        var result = await handler.Handle(new(subscription.Id, UserId.New(), Utc(15)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.CaptureNotResolved", result.Error.Code);
        Assert.Null(recorder.LastReason);
    }

    [Fact]
    public async Task Shutdown_for_a_free_period_has_nothing_to_refund()
    {
        // The capture resolves, but nothing was paid for the period, so the money
        // rule produces zero — distinct from "the capture is unresolvable".
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 0m));

        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(new(subscription.Id, UserId.New(), Utc(15)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NothingToRefund", result.Error.Code);
    }

    [Fact]
    public async Task Shutdown_with_a_non_utc_instant_is_malformed_input()
    {
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 300m));

        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(
            new(subscription.Id, UserId.New(), new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Local)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.MalformedInput", result.Error.Code);
    }

    [Fact]
    public async Task A_duplicate_charge_with_a_non_utc_instant_is_malformed_input()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        FamilySubscription subscription = SeedSubscriptionFor(db, family, plan, Utc(1));
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.LinkSubscription(subscription.Id);
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(
            new(owner, attempt.Id, "dup-key", RecordedOnUtc: new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Local)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.MalformedInput", result.Error.Code);
    }

    [Fact]
    public async Task A_duplicate_charge_without_a_subscription_link_is_malformed_input()
    {
        // M4: the subscription is the audit correlation. An attempt with no
        // server-side link cannot be attributed to a subscription, so it is
        // refused rather than refunded anonymously.
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 299m, 0m, 0m, 299m, 14m, 41.86m, 340.86m,
            null, SubscriptionPaymentMethod.Card, Utc(1));
        attempt.TryMarkSucceeded("txn-1", Utc(1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var handler = new RefundDuplicateSubscriptionChargeCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(new(owner, attempt.Id, "dup-key"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.MalformedInput", result.Error.Code);
    }

    [Fact]
    public async Task An_exhausted_capture_surfaces_as_ExceedsCaptured()
    {
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 300m));

        var recorder = new FakeRecorder { FailWith = "Finance.SubscriptionRefund.ExceedsCaptured" };
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        DateTime midpoint = Utc(1).AddTicks((Utc(1).AddMonths(1) - Utc(1)).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.ExceedsCaptured", result.Error.Code);
    }

    [Fact]
    public async Task A_missing_capture_surfaces_as_CaptureNotResolved()
    {
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 300m));

        var recorder = new FakeRecorder { FailWith = "Finance.SubscriptionRefund.CaptureRequired" };
        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, recorder);

        DateTime midpoint = Utc(1).AddTicks((Utc(1).AddMonths(1) - Utc(1)).Ticks / 2);
        var result = await handler.Handle(new(subscription.Id, UserId.New(), midpoint), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.CaptureNotResolved", result.Error.Code);
    }

    [Fact]
    public async Task A_non_current_subscription_cannot_be_shutdown_refunded()
    {
        await using FamiliesDbContext db = CreateContext();
        FamilySubscription subscription = await SeedSubscriptionAsync(
            db, Utc(1), Utc(1).AddMonths(1), (Utc(1), 300m));
        subscription.MarkNotCurrent();
        await db.SaveChangesAsync();

        var handler = new RefundSubscriptionServiceShutdownCommandHandler(db, new FakeRecorder());
        var result = await handler.Handle(new(subscription.Id, UserId.New(), Utc(15)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.NotCurrent", result.Error.Code);
    }

    [Fact]
    public async Task Cancel_renewal_records_a_zero_piastre_award_when_a_key_is_supplied()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, Utc(1), Utc(1).AddMonths(1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new CancelSubscriptionRenewalCommandHandler(db, recorder);

        var result = await handler.Handle(new(owner, "cancel-key"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionRefundReason.CancelRenewal, recorder.LastReason);
        Assert.Equal(0L, recorder.LastAwarded.MinorUnits);
        Assert.Equal("cancel-key", recorder.LastKey);
        // M4: a zero-piastre movement claims no budget, so it names no capture.
        Assert.Equal(subscription.Id, recorder.LastSubscriptionId);
        Assert.Equal(Guid.Empty, recorder.LastPaymentAttemptId);
        Assert.Equal(0L, recorder.LastCapturedMinorUnits);
        Assert.False(subscription.AutoRenewEnabled);
    }

    [Fact]
    public async Task Cancel_renewal_without_a_key_keeps_the_pre_existing_lifecycle()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, Utc(1), Utc(1).AddMonths(1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new CancelSubscriptionRenewalCommandHandler(db, recorder);

        var result = await handler.Handle(new(owner), default);

        Assert.True(result.IsSuccess);
        Assert.False(subscription.AutoRenewEnabled);
        Assert.Null(recorder.LastReason);
    }

    [Fact]
    public async Task Cancel_renewal_replay_with_a_key_on_an_already_cancelled_subscription_returns_the_recorded_outcome()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, Utc(1), Utc(1).AddMonths(1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder();
        var handler = new CancelSubscriptionRenewalCommandHandler(db, recorder);

        // First call cancels and records.
        var first = await handler.Handle(new(owner, "cancel-key"), default);
        Assert.True(first.IsSuccess);

        // M3: a replay WITH the key is not short-circuited to AlreadyRequested —
        // the recorder is re-invoked so the recorded outcome is re-readable.
        recorder.IsReplay = true;
        var replay = await handler.Handle(new(owner, "cancel-key"), default);

        Assert.True(replay.IsSuccess);
        Assert.Equal(SubscriptionRefundReason.CancelRenewal, recorder.LastReason);
        Assert.Equal("cancel-key", recorder.LastKey);

        // A keyless retry of the same already-cancelled subscription is still
        // AlreadyRequested (the pre-existing contract).
        var keyless = await handler.Handle(new(owner), default);
        Assert.True(keyless.IsFailure);
        Assert.Equal("Subscriptions.CancelRenewal.AlreadyRequested", keyless.Error.Code);
    }

    [Fact]
    public async Task A_finance_idempotency_conflict_does_not_roll_back_the_committed_cancellation()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, Utc(1), Utc(1).AddMonths(1));
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var recorder = new FakeRecorder { FailWith = "Finance.SubscriptionRefund.IdempotencyConflict" };
        var handler = new CancelSubscriptionRenewalCommandHandler(db, recorder);

        var result = await handler.Handle(new(owner, "cancel-key"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscriptions.Refund.IdempotencyConflict", result.Error.Code);
        // The lifecycle was already committed; the rejected recording never rolls it back.
        FamilySubscription stored = await db.FamilySubscriptions.SingleAsync();
        Assert.False(stored.AutoRenewEnabled);
        Assert.NotNull(stored.CancellationRequestedOnUtc);
    }

    /// <summary>
    /// Seeds a current subscription for an existing family and plan version so a
    /// payment attempt can be linked to it — every production settlement links the
    /// attempt server-side, and the subscription is the refund audit correlation.
    /// </summary>
    private static FamilySubscription SeedSubscriptionFor(
        FamiliesDbContext db,
        Family family,
        SubscriptionPlanVersion plan,
        DateTime createdOnUtc)
    {
        FamilySubscription subscription = FamilySubscription.Create(
            family.Id, plan, createdOnUtc, createdOnUtc.AddMonths(1));
        db.FamilySubscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Seeds a family, plan and current subscription plus one SUCCEEDED payment
    /// attempt per supplied <c>(settledOnUtc, payable)</c> pair, then applies the
    /// last one as the current period's charge snapshot — exactly as the real
    /// initial-capture and renewal paths do. Only attempts settling inside
    /// <c>[periodStart, periodEndsOnUtc)</c> are candidates for the current period,
    /// so earlier pairs model consumed earlier periods.
    /// </summary>
    private static async Task<FamilySubscription> SeedSubscriptionAsync(
        FamiliesDbContext db,
        DateTime createdOnUtc,
        DateTime periodEndsOnUtc,
        params (DateTime SettledOnUtc, decimal Payable)[] captures)
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(
            family.Id, plan, createdOnUtc, periodEndsOnUtc);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);

        SubscriptionPaymentAttempt? last = null;
        foreach ((DateTime settledOnUtc, decimal payable) in captures)
        {
            SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.Create(
                family.Id, plan, payable, 0m, 0m, payable, 14m, 41.86m, payable,
                null, SubscriptionPaymentMethod.Card, settledOnUtc);
            attempt.LinkSubscription(subscription.Id);
            attempt.TryMarkSucceeded($"txn-{Guid.NewGuid():N}", settledOnUtc);
            db.SubscriptionPaymentAttempts.Add(attempt);
            last = attempt;
        }

        if (last is not null)
            subscription.SetCurrentPeriodChargeSnapshot(last);

        await db.SaveChangesAsync();
        return subscription;
    }

    private static FamiliesDbContext CreateContext() => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeRecorder : ISubscriptionRefundRecorder
    {
        public Money LastAwarded { get; private set; } = Money.Zero;
        public SubscriptionRefundReason? LastReason { get; private set; }
        public string? LastKey { get; private set; }
        public Guid? LastSubscriptionId { get; private set; }
        public Guid? LastPaymentAttemptId { get; private set; }
        public long LastCapturedMinorUnits { get; private set; }
        public string? FailWith { get; set; }
        public bool IsReplay { get; set; }

        public Task<Result<SubscriptionRefundSnapshot>> RecordAsync(
            Guid refundCaseId,
            Guid familyId,
            Guid subscriptionId,
            Guid paymentAttemptId,
            long capturedMinorUnits,
            Money awarded,
            SubscriptionRefundReason reason,
            string? idempotencyKey,
            DateTime recordedOnUtc,
            Guid? actorUserId,
            string? evidenceReference,
            CancellationToken cancellationToken)
        {
            LastSubscriptionId = subscriptionId;
            LastPaymentAttemptId = paymentAttemptId;
            LastCapturedMinorUnits = capturedMinorUnits;
            LastAwarded = awarded;
            LastReason = reason;
            LastKey = idempotencyKey;

            if (FailWith is not null)
            {
                return Task.FromResult(Result<SubscriptionRefundSnapshot>.Failure(
                    new Error(FailWith, "rejected")));
            }

            return Task.FromResult(Result<SubscriptionRefundSnapshot>.Success(
                new SubscriptionRefundSnapshot(
                    refundCaseId, awarded.MinorUnits, Money.CurrencyCode, reason, IsReplay,
                    RefundLedgerEntryType.Approved, recordedOnUtc)));
        }
    }
}
