using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;
using Ahlena.Modules.Finance.Infrastructure;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// W3-F3 subscription-refund seam: its M7 idempotency (Owner decisions #35
/// movement semantics; design pin item 4: a column-level idempotency key plus a
/// unique index, never a receipts table) and its M4 capture-scoped refundable
/// budget (Owner decisions #4 and #6: never refund more than the funds captured
/// for <em>that</em> period, less previous refunds).
/// <para>
/// A replay with the same key and the same content returns the original outcome
/// (<c>isReplay: true</c>) without claiming the budget again; the same key with
/// different content is the stable conflict; and the three clause-9 reasons carry
/// distinct reason tokens so they stay attributable.
/// </para>
/// <para>
/// The budget is keyed by CAPTURE, so the multi-period counterexample — refund a
/// current-period duplicate charge, then half of the same current period, and
/// hope an older period's consumed money covers the difference — must be refused
/// in both orders. Relational concurrency (real row locks, the unique capture
/// index) is <b>not</b> demonstrated by this in-memory suite: that proof belongs to
/// the approved disposable PostgreSQL gate and is deliberately not claimed here.
/// </para>
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class SubscriptionRefundRecorderTests
{
    private static readonly Guid Family = Guid.Parse("0198e2c2-4444-7777-8888-000000000201");
    private static readonly Guid Case = Guid.Parse("0198e2c2-4444-7777-8888-000000000202");
    private static readonly Guid Actor = Guid.Parse("0198e2c2-4444-7777-8888-000000000203");
    private static readonly Guid Subscription = Guid.Parse("0198e2c2-4444-7777-8888-000000000204");

    /// <summary>The capture that paid for January — an older, already-consumed period.</summary>
    private static readonly Guid JanuaryCapture = Guid.Parse("0198e2c2-4444-7777-8888-000000000205");

    /// <summary>The capture that paid for the current period.</summary>
    private static readonly Guid CurrentCapture = Guid.Parse("0198e2c2-4444-7777-8888-000000000206");

    private static DateTime Utc(int hour) => new(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_full_refund_is_recorded_as_a_movement_entry()
    {
        await using FinanceDbContext db = CreateContext();

        var result = await Record(
            db, Case, CurrentCapture, 15_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(15000L, result.Value.AwardedMinorUnits);
        Assert.Equal("EGP", result.Value.Currency);
        Assert.Equal(SubscriptionRefundReason.DuplicateCharge, result.Value.Reason);
        Assert.Equal(RefundLedgerEntryType.Approved, result.Value.EntryType);
        Assert.False(result.Value.IsReplay);

        // The ledger records the MOVEMENT (#35): the awarded amount, never a
        // resulting balance.
        RefundLedgerEntry entry = Assert.Single(db.RefundLedgerEntries.ToList());
        Assert.Equal(15000L, entry.Amount.MinorUnits);
        Assert.Contains("reason:subscription-duplicate-charge", entry.EvidenceReference);
    }

    [Fact]
    public async Task The_three_clause_nine_reasons_carry_distinct_tokens()
    {
        await using FinanceDbContext db = CreateContext();

        await Record(db, Guid.NewGuid(), CurrentCapture, 100_000L, 1m,
            SubscriptionRefundReason.CancelRenewal, null, Utc(9));
        await Record(db, Guid.NewGuid(), CurrentCapture, 100_000L, 1m,
            SubscriptionRefundReason.DuplicateCharge, null, Utc(9));
        await Record(db, Guid.NewGuid(), CurrentCapture, 100_000L, 1m,
            SubscriptionRefundReason.ServiceShutdown, null, Utc(9));

        string[] tokens = db.RefundLedgerEntries.Select(entry => entry.EvidenceReference!).ToArray();
        Assert.Contains("reason:subscription-cancel-renewal", tokens);
        Assert.Contains("reason:subscription-duplicate-charge", tokens);
        Assert.Contains("reason:subscription-service-shutdown", tokens);
    }

    [Fact]
    public async Task A_zero_piastre_cancel_renewal_award_is_recorded_as_computed_and_consumes_no_budget()
    {
        await using FinanceDbContext db = CreateContext();
        var recorder = new SubscriptionRefundRecorder(db);

        var result = await recorder.RecordAsync(
            Case, Family, Subscription, Guid.Empty, 0L, Money.Zero,
            SubscriptionRefundReason.CancelRenewal,
            "key-cancel", Utc(9), Actor, null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0L, result.Value.AwardedMinorUnits);
        Assert.Single(db.RefundLedgerEntries.ToList());
        // An award that moves no money claims nothing, so it does not even open a
        // budget row and needs no capture.
        Assert.Empty(db.SubscriptionRefundBudgets.ToList());
    }

    [Fact]
    public async Task A_funded_award_without_a_capture_is_refused()
    {
        // The ceiling is per capture, so a funded award that cannot name one has
        // nothing to serialize on: refused as malformed rather than silently
        // falling back to a subscription-wide total.
        await using FinanceDbContext db = CreateContext();
        var recorder = new SubscriptionRefundRecorder(db);

        var result = await recorder.RecordAsync(
            Case, Family, Subscription, Guid.Empty, 15_000L, Money.FromMajorUnits(150m),
            SubscriptionRefundReason.DuplicateCharge,
            "key-no-capture", Utc(9), Actor, null, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.CaptureRequired", result.Error.Code);
        Assert.Empty(db.RefundLedgerEntries.ToList());
        Assert.Empty(db.SubscriptionRefundBudgets.ToList());
    }

    [Fact]
    public async Task Replay_with_the_same_key_and_content_returns_the_original_outcome()
    {
        await using FinanceDbContext db = CreateContext();

        var first = await Record(db, Case, CurrentCapture, 15_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-1", Utc(9));
        var replay = await Record(db, Case, CurrentCapture, 15_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-1", Utc(10));

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.IsReplay);
        Assert.Equal(first.Value.AwardedMinorUnits, replay.Value.AwardedMinorUnits);
        // No second movement was appended, and the replay claimed no extra budget.
        Assert.Single(db.RefundLedgerEntries.ToList());
        Assert.Single(db.SubscriptionRefundRequests.ToList());
        Assert.Equal(15_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task Replay_with_a_different_amount_is_an_idempotency_conflict()
    {
        await using FinanceDbContext db = CreateContext();

        await Record(db, Case, CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-1", Utc(9));
        var conflict = await Record(db, Case, CurrentCapture, 30_000L, 99m,
            SubscriptionRefundReason.DuplicateCharge, "key-1", Utc(10));

        Assert.True(conflict.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.IdempotencyConflict", conflict.Error.Code);
        Assert.Single(db.RefundLedgerEntries.ToList());
        // A rejected replay does not touch the budget.
        Assert.Equal(15_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task A_fresh_key_for_an_already_recorded_case_is_refused()
    {
        await using FinanceDbContext db = CreateContext();

        await Record(db, Case, CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-1", Utc(9));
        var second = await Record(db, Case, CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.DuplicateCharge, "key-2", Utc(10));

        Assert.True(second.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.IdempotencyConflict", second.Error.Code);
        Assert.Equal(15_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task A_null_actor_is_refused_as_invalid_and_appends_no_movement()
    {
        // B2: the frozen RefundLedger.Approve records a MOVEMENT actor on a
        // non-nullable Guid. A null/empty actor on this path is malformed input,
        // never a silent system move, so the recorder refuses it and no movement
        // is appended.
        await using FinanceDbContext db = CreateContext();
        var recorder = new SubscriptionRefundRecorder(db);

        var result = await recorder.RecordAsync(
            Case, Family, Subscription, CurrentCapture, 15_000L, Money.FromMajorUnits(150m),
            SubscriptionRefundReason.DuplicateCharge,
            "key-null-actor", Utc(9), null, null, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.Invalid", result.Error.Code);
        Assert.Empty(db.RefundLedgerEntries.ToList());
        Assert.Empty(db.SubscriptionRefundBudgets.ToList());
    }

    [Fact]
    public async Task The_idempotency_key_is_bounded()
    {
        await using FinanceDbContext db = CreateContext();

        var result = await Record(
            db, Case, CurrentCapture, 10_000L, 1m,
            SubscriptionRefundReason.DuplicateCharge,
            new string('k', SubscriptionRefundRequest.MaximumIdempotencyKeyLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task The_recorder_is_scoped_by_family_so_the_same_key_across_families_does_not_collide()
    {
        await using FinanceDbContext db = CreateContext();
        var recorder = new SubscriptionRefundRecorder(db);

        var one = await recorder.RecordAsync(Guid.NewGuid(), Family, Subscription, CurrentCapture,
            10_000L, Money.FromMajorUnits(10m),
            SubscriptionRefundReason.DuplicateCharge, "shared-key", Utc(9), Actor, null, default);
        var two = await recorder.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), Subscription, JanuaryCapture,
            20_000L, Money.FromMajorUnits(20m),
            SubscriptionRefundReason.DuplicateCharge, "shared-key", Utc(9), Actor, null, default);

        Assert.True(one.IsSuccess);
        Assert.True(two.IsSuccess);
        Assert.False(two.Value.IsReplay);
        Assert.Equal(2, db.SubscriptionRefundBudgets.Count());
    }

    // ---------------------------------------------------------------------
    // M4: the ceiling is the CAPTURE, not the subscription.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_charge_then_shutdown_cannot_refund_the_same_capture_twice()
    {
        // One capture of 300 for the current period. The duplicate-charge case
        // refunds it in full; the shutdown case against the same capture must be
        // refused, because that capture's money is already claimed.
        await using FinanceDbContext db = CreateContext();

        var duplicate = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 300m,
            SubscriptionRefundReason.DuplicateCharge, "dup-1", Utc(9));
        var shutdown = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.ServiceShutdown, "shutdown-1", Utc(10));

        Assert.True(duplicate.IsSuccess);
        Assert.True(shutdown.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", shutdown.Error.Code);
        Assert.Single(db.RefundLedgerEntries.ToList());
        Assert.Equal(30_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task Shutdown_then_duplicate_charge_cannot_refund_the_same_capture_twice()
    {
        // The reverse order: the shutdown claims half the capture, so the full
        // duplicate-charge refund no longer fits and is refused rather than
        // silently trimmed (a trimmed duplicate charge is not "in full").
        await using FinanceDbContext db = CreateContext();

        var shutdown = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.ServiceShutdown, "shutdown-1", Utc(9));
        var duplicate = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 300m,
            SubscriptionRefundReason.DuplicateCharge, "dup-1", Utc(10));

        Assert.True(shutdown.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", duplicate.Error.Code);
        Assert.Single(db.RefundLedgerEntries.ToList());
        Assert.Equal(15_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task An_older_period_supplies_no_headroom_for_the_current_one()
    {
        // The exact counterexample. January captured 300 (P1) and is fully
        // consumed; the current period captured 300 (P2). Under a
        // subscription-scoped, lifetime-seeded budget, refunding P2's duplicate
        // charge (300) and then half of P2 (150) "fits" in 600 and pays 450 — the
        // current period is refunded twice on January's headroom. Capture-scoped,
        // the second claim is refused in both orders, and January's row is never
        // touched.
        await using (FinanceDbContext forward = CreateContext())
        {
            var duplicate = await Record(forward, Guid.NewGuid(), CurrentCapture, 30_000L, 300m,
                SubscriptionRefundReason.DuplicateCharge, "dup-p2", Utc(9));
            var shutdown = await Record(forward, Guid.NewGuid(), CurrentCapture, 30_000L, 150m,
                SubscriptionRefundReason.ServiceShutdown, "shutdown-p2", Utc(14));

            Assert.True(duplicate.IsSuccess);
            Assert.True(shutdown.IsFailure);
            Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", shutdown.Error.Code);

            SubscriptionRefundBudget budget = Assert.Single(forward.SubscriptionRefundBudgets.ToList());
            Assert.Equal(CurrentCapture, budget.PaymentAttemptId);
            Assert.Equal(30_000L, budget.ClaimedMinorUnits);
            // January never entered the picture.
            Assert.False(forward.SubscriptionRefundBudgets.Any(item => item.PaymentAttemptId == JanuaryCapture));
        }

        await using (FinanceDbContext reverse = CreateContext())
        {
            var shutdown = await Record(reverse, Guid.NewGuid(), CurrentCapture, 30_000L, 150m,
                SubscriptionRefundReason.ServiceShutdown, "shutdown-p2", Utc(9));
            var duplicate = await Record(reverse, Guid.NewGuid(), CurrentCapture, 30_000L, 300m,
                SubscriptionRefundReason.DuplicateCharge, "dup-p2", Utc(14));

            Assert.True(shutdown.IsSuccess);
            Assert.True(duplicate.IsFailure);
            Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", duplicate.Error.Code);

            SubscriptionRefundBudget budget = Assert.Single(reverse.SubscriptionRefundBudgets.ToList());
            Assert.Equal(15_000L, budget.ClaimedMinorUnits);
        }
    }

    [Fact]
    public async Task Duplicate_charges_of_different_captures_are_isolated()
    {
        // January's duplicate charge and the current period's duplicate charge are
        // different captures: each may be refunded in full, and neither consumes
        // the other's funds.
        await using FinanceDbContext db = CreateContext();

        var january = await Record(db, Guid.NewGuid(), JanuaryCapture, 30_000L, 300m,
            SubscriptionRefundReason.DuplicateCharge, "dup-p1", Utc(9));
        var current = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 300m,
            SubscriptionRefundReason.DuplicateCharge, "dup-p2", Utc(9));

        Assert.True(january.IsSuccess);
        Assert.True(current.IsSuccess);
        Assert.All(db.SubscriptionRefundBudgets.ToList(),
            budget => Assert.Equal(30_000L, budget.ClaimedMinorUnits));
        Assert.Equal(2, db.SubscriptionRefundBudgets.Count());
    }

    [Fact]
    public async Task A_plan_change_capture_is_its_own_budget()
    {
        // A paid plan change settles a new capture for the target period. It has
        // its own ceiling, so the initial capture's awards neither consume it nor
        // fund it.
        await using FinanceDbContext db = CreateContext();

        var initial = await Record(db, Guid.NewGuid(), JanuaryCapture, 34_200L, 342m,
            SubscriptionRefundReason.DuplicateCharge, "dup-initial", Utc(9));
        var change = await Record(db, Guid.NewGuid(), CurrentCapture, 40_000L, 300m,
            SubscriptionRefundReason.DuplicateCharge, "dup-change", Utc(9));

        Assert.True(initial.IsSuccess);
        Assert.True(change.IsSuccess);

        SubscriptionRefundBudget initialBudget = Assert.Single(
            db.SubscriptionRefundBudgets.Where(item => item.PaymentAttemptId == JanuaryCapture));
        Assert.Equal(34_200L, initialBudget.CapturedMinorUnits);
        Assert.Equal(34_200L, initialBudget.ClaimedMinorUnits);

        SubscriptionRefundBudget changeBudget = Assert.Single(
            db.SubscriptionRefundBudgets.Where(item => item.PaymentAttemptId == CurrentCapture));
        Assert.Equal(40_000L, changeBudget.CapturedMinorUnits);
        Assert.Equal(30_000L, changeBudget.ClaimedMinorUnits);
    }

    [Fact]
    public async Task Different_keys_targeting_one_capture_accumulate_cumulatively()
    {
        // Different cases and different keys against the SAME capture: per-key
        // uniqueness alone cannot serialize these, so the capture budget must.
        // Captured = 250; two 100 awards fit, the third does not.
        await using FinanceDbContext db = CreateContext();

        var first = await Record(db, Guid.NewGuid(), CurrentCapture, 25_000L, 100m,
            SubscriptionRefundReason.DuplicateCharge, "k1", Utc(9));
        var second = await Record(db, Guid.NewGuid(), CurrentCapture, 25_000L, 100m,
            SubscriptionRefundReason.ServiceShutdown, "k2", Utc(9));
        var third = await Record(db, Guid.NewGuid(), CurrentCapture, 25_000L, 100m,
            SubscriptionRefundReason.DuplicateCharge, "k3", Utc(9));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(third.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", third.Error.Code);
        Assert.Equal(20_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
        Assert.Equal(2, db.RefundLedgerEntries.Count());
    }

    [Fact]
    public async Task A_refused_claim_leaves_the_budget_usable_for_a_smaller_later_award()
    {
        // The refusal must not corrupt the counter: 70 of 100 claimed, a 50 award
        // is refused, and a 30 award still fits exactly.
        await using FinanceDbContext db = CreateContext();

        var first = await Record(db, Guid.NewGuid(), CurrentCapture, 10_000L, 70m,
            SubscriptionRefundReason.DuplicateCharge, "k1", Utc(9));
        var refused = await Record(db, Guid.NewGuid(), CurrentCapture, 10_000L, 50m,
            SubscriptionRefundReason.ServiceShutdown, "k2", Utc(9));
        var fitted = await Record(db, Guid.NewGuid(), CurrentCapture, 10_000L, 30m,
            SubscriptionRefundReason.ServiceShutdown, "k3", Utc(9));

        Assert.True(first.IsSuccess);
        Assert.True(refused.IsFailure);
        Assert.Equal("Finance.SubscriptionRefund.ExceedsCaptured", refused.Error.Code);
        Assert.True(fitted.IsSuccess);
        Assert.Equal(10_000L, Assert.Single(db.SubscriptionRefundBudgets.ToList()).ClaimedMinorUnits);
    }

    [Fact]
    public async Task Capacity_only_grows_so_a_stale_or_incremental_reader_cannot_shrink_it()
    {
        // The same capture can be described by two money bases (the attempt's
        // incremental payable and the period's consideration, which for a paid
        // plan change is the target gross). A later, smaller reading must not lower
        // a ceiling another case already relied on.
        await using FinanceDbContext db = CreateContext();

        var first = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 150m,
            SubscriptionRefundReason.ServiceShutdown, "k1", Utc(9));
        var second = await Record(db, Guid.NewGuid(), CurrentCapture, 40_000L, 50m,
            SubscriptionRefundReason.ServiceShutdown, "k2", Utc(10));
        // A stale reading of the same capture (smaller than what is already known)
        // may not erase capacity.
        var third = await Record(db, Guid.NewGuid(), CurrentCapture, 30_000L, 5m,
            SubscriptionRefundReason.ServiceShutdown, "k3", Utc(11));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);

        SubscriptionRefundBudget budget = Assert.Single(db.SubscriptionRefundBudgets.ToList());
        Assert.Equal(40_000L, budget.CapturedMinorUnits);
        Assert.Equal(20_500L, budget.ClaimedMinorUnits);
        Assert.Equal(19_500L, budget.RemainingMinorUnits);
    }

    private static Task<Result<SubscriptionRefundSnapshot>> Record(
        FinanceDbContext db,
        Guid caseId,
        Guid captureId,
        long capturedMinorUnits,
        decimal awardMajorUnits,
        SubscriptionRefundReason reason,
        string? key,
        DateTime? on = null)
    {
        var recorder = new SubscriptionRefundRecorder(db);
        return recorder.RecordAsync(
            caseId,
            Family,
            Subscription,
            captureId,
            capturedMinorUnits,
            Money.FromMajorUnits(awardMajorUnits),
            reason,
            key,
            on ?? Utc(9),
            Actor,
            null,
            default);
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
