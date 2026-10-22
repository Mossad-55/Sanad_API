#pragma warning disable xUnit1031 // The harness drives the executor's synchronous facade; these tests are single-threaded.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Rentals.Application;
using Sanad.Modules.Rentals.Domain;
using Sanad.Modules.Rentals.Infrastructure;
using Xunit;
using RefundExecutionResult = Sanad.Modules.Rentals.Application.RentalRefundExecutor.RefundExecutionResult;
using RefundOutcome = Sanad.Modules.Rentals.Application.RentalRefundExecutor.RefundOutcome;

namespace Sanad.UnitTests.Rentals;

/// <summary>
/// W3-F4 crash-safety corrections (review B1/B2, M1–M3, M5): every retry
/// classifies the stored Finance case first, an ambiguous provider outcome is
/// durably timed out and NEVER resent, a recorded completion replays without
/// moving money again, and the app credit posts exactly once per refund case
/// with the authoritative Approved + CreditBalancePosted entry pair under the
/// refund case id.
/// <para>
/// Backed by REAL SQLite in-memory contexts (a relational provider — the
/// booking version token and the ledger's unique (case, sequence) index are
/// actual database constraints here; Rentals and Finance use SEPARATE
/// connections/databases, mirroring the deployed module split). Authored by
/// hand and marked NOT RUN: executing them is the Mastermind's build/test slot.
/// Concurrency coverage is SEQUENTIAL against the relational provider (two
/// independent contexts; the second SaveChanges must fail) — true parallel
/// scheduling is deliberately not claimed from SQLite; that proof belongs to
/// the Postgres gate.
/// </para>
/// </summary>
public sealed class RentalRefundExecutorTests
{
    private const long DailyRate = 12_000;
    private const long DepositAmount = 50_000;

    private static readonly DateTime Base = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed class Harness : IDisposable
    {
        public SqliteConnection RentalsConnection { get; }
        public SqliteConnection FinanceConnection { get; }
        public RentalsDbContext Rentals { get; }
        public FinanceDbContext Finance { get; }
        public RentalRefundRecorder Recorder { get; }
        public ScriptedPaymob Paymob { get; } = new();

        public Harness()
        {
            // Separate connections AND separate databases: the two modules own
            // distinct stores in deployment, and EnsureCreated on a shared
            // connection would blur the module boundary the seam enforces.
            RentalsConnection = new SqliteConnection("DataSource=:memory:");
            FinanceConnection = new SqliteConnection("DataSource=:memory:");
            RentalsConnection.Open();
            FinanceConnection.Open();

            // The Finance model carries a PostgreSQL check constraint that uses
            // char_length; SQLite has no such built-in. Register the portable
            // equivalent so EnsureCreated can build the same relational guard.
            FinanceConnection.CreateFunction(
                "char_length",
                (string? value) => value?.Length ?? 0);

            var rentalsOptions = new DbContextOptionsBuilder<RentalsDbContext>()
                .UseSqlite(RentalsConnection)
                .Options;
            var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlite(FinanceConnection)
                .Options;

            Rentals = new RentalsDbContext(rentalsOptions);
            Finance = new FinanceDbContext(financeOptions);
            Rentals.Database.EnsureCreated();
            Finance.Database.EnsureCreated();

            Recorder = new RentalRefundRecorder(Finance);
        }

        /// <summary>A confirmed booking: captured = plannedDays × rate + deposit, one succeeded transaction.</summary>
        public RentalBooking SeedCapturedBooking(int plannedDays)
        {
            RentalEquipment equipment = RentalEquipment.Create(
                $"rig-{Guid.NewGuid():N}", "جهاز اختبار", null, null, DailyRate, DepositAmount, 30, Utc(1, 8));
            Rentals.RentalEquipment.Add(equipment);

            RentalBooking booking = RentalBooking.Create(
                equipment, Guid.CreateVersion7(), Guid.CreateVersion7(), plannedDays, null, Utc(1, 8));
            RentalPaymentTransaction transaction = booking.OpenPaymentTransaction($"rnt-{Guid.NewGuid():N}", RentalPaymentMethod.Card, Utc(1, 8));
            booking.ConfirmPayment(transaction, 424_242, Utc(1, 9));
            Rentals.RentalBookings.Add(booking);
            Rentals.SaveChanges();

            Paymob.GatewayTransactionId = transaction.PaymobTransactionId ?? "paymob-txn-1";
            return booking;
        }

        /// <summary>Persists the booking mutation and its claim atomically (the handler's claim step).</summary>
        public void SaveBooking(RentalBooking booking)
        {
            Rentals.SaveChanges();
            Paymob.GatewayTransactionId = booking.SucceededPaymentTransaction()?.PaymobTransactionId
                ?? Paymob.GatewayTransactionId;
        }

        public void Dispose()
        {
            Rentals.Dispose();
            Finance.Dispose();
            RentalsConnection.Dispose();
            FinanceConnection.Dispose();
        }
    }

    private sealed class ScriptedPaymob : IPaymobClient
    {
        public int RefundCalls { get; private set; }
        public string GatewayTransactionId { get; set; } = "paymob-txn-1";
        public string? SuccessReference { get; set; } = "prov-ref-1";
        public Error? RefundError { get; set; }
        public InvalidOperationException? Throw { get; set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The executor tests never open payment intents.");

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            Assert.Equal(GatewayTransactionId, paymobTransactionId);
            if (Throw is not null)
                throw Throw;

            return Task.FromResult(
                RefundError is not null
                    ? Result<string?>.Failure(RefundError)
                    : Result<string?>.Success(SuccessReference));
        }
    }

    /// <summary>Drives the executor the way the three handlers do, with the composed-evidence payload.</summary>
    private static RefundExecutionResult Run(
        Harness harness,
        RentalBooking booking,
        Guid refundCaseId,
        long claimedAmount,
        RentalRefundDestination destination,
        RentalRefundReason reason,
        string? idempotencyKey)
    {
        string evidence = RentalRefundExecutor.ComposeEvidence(
            booking.Id, RentalRefundMovementKind.CancellationFullRefund, destination, idempotencyKey);

        return RentalRefundExecutor.ExecuteOrResumeAsync(
            reason,
            refundCaseId,
            claimedAmount,
            booking.FamilyId,
            Guid.CreateVersion7(),
            Base,
            evidence,
            harness.Paymob.GatewayTransactionId,
            idempotencyKey,
            destination,
            harness.Paymob,
            harness.Recorder,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Persists a families-owned CAPTURED booking for handler-level tests (B1/Mastermind preserve).</summary>
    private static RentalBooking SeedFamiliesCapturedBooking(Harness harness, Family family, int plannedDays)
    {
        RentalEquipment equipment = RentalEquipment.Create(
            $"rig-{Guid.NewGuid():N}", "جهاز اختبار", null, null, DailyRate, DepositAmount, 30, Utc(1, 8));
        harness.Rentals.RentalEquipment.Add(equipment);

        RentalBooking booking = RentalBooking.Create(equipment, family.Id.Value, family.OwnerUserId.Value, plannedDays, null, Utc(1, 8));
        RentalPaymentTransaction transaction = booking.OpenPaymentTransaction($"rnt-{Guid.NewGuid():N}", RentalPaymentMethod.Card, Utc(1, 8));
        booking.ConfirmPayment(transaction, 424_242, Utc(1, 9));
        harness.Rentals.RentalBookings.Add(booking);
        harness.Rentals.SaveChanges();

        harness.Paymob.GatewayTransactionId = transaction.PaymobTransactionId ?? "paymob-txn-1";
        return booking;
    }

    private static FamiliesDbContext NewFamiliesDatabase(string name)
    {
        var familiesOptions = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .Options;
        FamiliesDbContext families = new(familiesOptions);
        return families;
    }

    [Fact]
    public void A_definite_rails_refusal_signs_the_reversal_releases_the_claim_and_the_retry_starts_fresh()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        harness.Paymob.RefundError = new Error("Paymob.RefundRejected", "The refund was refused by the provider.");

        RefundExecutionResult first = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");

        // Definitive refusal: signed reversal on the ledger, retryable outcome, claim releasable.
        Assert.Equal(RefundOutcome.FailedRetryable, first.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.Failed, timeline.Value!.LatestEntryType);
        Assert.Equal(0, timeline.Value.NetRecordedMinorUnits); // approval + signed reversal nets to zero
        Assert.Equal(0, timeline.Value.TotalCompletedMinorUnits);

        // The handler release + a fresh claim with a NEW case id starts cleanly.
        booking.ReleaseRefundClaim(Utc(1, 11));
        harness.SaveBooking(booking);

        Guid freshCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, freshCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 12));
        harness.SaveBooking(booking);

        harness.Paymob.RefundError = null;
        RefundExecutionResult retry = Run(
            harness, booking, freshCaseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-2");

        Assert.Equal(RefundOutcome.Committed, retry.Outcome);
        Assert.False(retry.IsReplay);
        Assert.Equal("prov-ref-1", retry.GatewayReference);
        Assert.Equal(2, harness.Paymob.RefundCalls); // the retry is a genuinely new movement case
    }

    [Fact]
    public void An_unknown_rails_outcome_records_a_durable_timeout_and_never_resends()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        harness.Paymob.RefundError = new Error("Paymob.RefundOutcomeUnknown", "The refund outcome could not be confirmed.");

        RefundExecutionResult first = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");

        // Ambiguous outcome: durable timeout with the provider signal, blocked, no reversal.
        Assert.Equal(RefundOutcome.BlockedUnknown, first.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.TimedOut, timeline.Value!.LatestEntryType);
        Assert.Equal("Paymob.RefundOutcomeUnknown", timeline.Value.LatestProviderReference);
        // The frozen ledger's net counts the approval AND the ambiguous timeout:
        // the case is deliberately unresolved until operations reconciles it (#17).
        Assert.Equal(2 * captured, timeline.Value.NetRecordedMinorUnits);

        // A matching retry classifies and blocks — no second provider call (#17).
        RefundExecutionResult retry = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.BlockedUnknown, retry.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);

        // A different-content retry is also blocked; the pending claim is untouched.
        RefundExecutionResult foreign = Run(
            harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
            RentalRefundReason.CancellationBeforeDelivery, "other-key");
        Assert.Equal(RefundOutcome.BlockedUnknown, foreign.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);
        Assert.Equal(RentalRefundMovementKind.CancellationFullRefund, booking.PendingRefundKind);
        Assert.Equal(caseId, booking.PendingRefundCaseId);
    }

    [Fact]
    public void A_provider_call_that_throws_records_the_durable_timeout_and_never_resends()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        // B2: ANY exception escaping the provider client — transport fault, host
        // crash mid-call, cancellation — is an AMBIGUOUS outcome, never a refusal.
        harness.Paymob.Throw = new InvalidOperationException("the transport socket exploded");

        RefundExecutionResult first = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");

        // The exception was normalised to the unknown taxonomy, durably timed out.
        Assert.Equal(RefundOutcome.BlockedUnknown, first.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.TimedOut, timeline.Value!.LatestEntryType);
        Assert.Equal("Paymob.RefundOutcomeUnknown", timeline.Value.LatestProviderReference);

        // The resume re-classifies the durable fence: blocked, never a resend.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.BlockedUnknown, resumed.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls);
    }

    [Fact]
    public void An_approval_left_by_a_crash_is_never_resent_and_different_content_refuses()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        // CRASH WINDOW: the Finance approval committed, then the process died
        // before (or somewhere within) the provider call.
        Result<RentalRefundMovementSnapshot> approval = harness.Recorder.RecordTransferAsync(
            caseId,
            Money.FromMinorUnits(captured),
            RentalRefundReason.CancellationBeforeDelivery,
            booking.RequestedByUserId,
            Utc(1, 10),
            "key-1",
            providerReference: null,
            RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund,
                RentalRefundDestination.OriginalPaymentMethod, "key-1"),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(approval.IsSuccess);

        // A DIFFERENT-content retry refuses BEFORE anything else happens — the
        // pending claim is never erased or re-routed by different content.
        RefundExecutionResult foreign = Run(
            harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
            RentalRefundReason.CancellationBeforeDelivery, "key-2");
        Assert.Equal(RefundOutcome.BlockedUnknown, foreign.Outcome);
        Assert.Equal("Rentals.Refund.AlreadyRecorded", foreign.Failure!.Code);
        Assert.Equal(0, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> beforeResume =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Single(beforeResume.Value!.Entries); // the refusal did not append anything

        // The SAME-content resume classifies the unprovable outcome: timeout + blocked.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.BlockedUnknown, resumed.Outcome);
        Assert.Equal(0, harness.Paymob.RefundCalls); // nothing was ever resent

        Result<RentalRefundCaseSnapshot?> afterResume =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.TimedOut, afterResume.Value!.LatestEntryType);

        // And once timed out, further retries stay blocked — never a resend.
        RefundExecutionResult again = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.BlockedUnknown, again.Outcome);
        Assert.Equal(0, harness.Paymob.RefundCalls);
    }

    [Fact]
    public void A_confirmed_completion_replays_after_a_local_save_crash_without_reexecuting()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        RefundExecutionResult first = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.Equal("prov-ref-1", first.GatewayReference);
        Assert.Equal(1, harness.Paymob.RefundCalls);

        // CRASH WINDOW: Finance committed (approval + confirmed transfer) but the
        // booking movement row never landed. The resume replays the stored outcome.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, resumed.Outcome);
        Assert.True(resumed.IsReplay);
        Assert.Equal("prov-ref-1", resumed.GatewayReference);
        Assert.Equal(1, harness.Paymob.RefundCalls); // the rails ran exactly once, ever

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(1, timeline.Value!.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.Approved));
        Assert.Equal(1, timeline.Value.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.TransferConfirmed));
        // The frozen ledger's completed total (confirmed transfers + posted credit)
        // agrees with the award; the signed net double-counts approval + completion by design (#4).
        Assert.Equal(captured, timeline.Value.TotalCompletedMinorUnits);
    }

    [Fact]
    public void A_completed_credit_case_retried_as_the_rails_replays_the_stored_credit_not_a_transfer()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.AppCredit, Utc(1, 10));
        harness.SaveBooking(booking);

        RefundExecutionResult credit = Run(
            harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, credit.Outcome);
        Assert.Equal(0, harness.Paymob.RefundCalls); // credit never touches the rails

        // M3 destination binding: the family re-asks toward the rails with a
        // different key — the recorded CREDIT outcome replays under its recorded
        // destination (AppCredit), never a transfer, never another movement.
        RefundExecutionResult retried = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "different-key");
        Assert.Equal(RefundOutcome.Committed, retried.Outcome);
        Assert.True(retried.IsReplay);
        Assert.Equal(RentalRefundDestination.AppCredit, retried.Destination);
        Assert.Null(retried.GatewayReference);
        Assert.Equal(0, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(0, timeline.Value!.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.TransferConfirmed));
        Assert.Equal(1, timeline.Value.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.CreditBalancePosted));
    }

    [Fact]
    public void App_credit_posts_exactly_once_with_an_approved_pair_and_a_same_case_retry_replays()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.AppCredit, Utc(1, 10));
        harness.SaveBooking(booking);

        RefundExecutionResult first = Run(
            harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, first.Outcome);
        Assert.False(first.IsReplay);

        // The credit landed atomically: balance reflects the award, the ledger
        // carries the authoritative Approved entry AND the attributable
        // CreditBalancePosted entry (M1 closure — both in ONE Finance unit of
        // work under the refund case id).
        CreditBalance balance = harness.Finance.CreditBalances.Single();
        Assert.Equal(captured, balance.Balance.MinorUnits);

        Result<RentalRefundCaseSnapshot?> firstTimeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(1, firstTimeline.Value!.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.Approved));
        Assert.Equal(1, firstTimeline.Value.Entries.Count(entry => entry.EntryType == RefundLedgerEntryType.CreditBalancePosted));
        Assert.Equal(2, firstTimeline.Value.Entries.Count);
        // The frozen net counts approval + posted credit; completed carries the post.
        Assert.Equal(2 * captured, firstTimeline.Value.NetRecordedMinorUnits);
        Assert.Equal(captured, firstTimeline.Value.TotalCompletedMinorUnits);
        // The proof token binds family + kind + destination on the recorded entries.
        string? proof = firstTimeline.Value.Entries
            .Single(entry => entry.EntryType == RefundLedgerEntryType.Approved)
            .EvidenceReference;
        Assert.Contains(";family:", proof);
        Assert.Contains(";kind:CancellationFullRefund", proof);
        Assert.Contains(";dest:AppCredit", proof);

        // INTERRUPTED LOCAL SAVE: the resume replays the recorded credit instead of
        // posting again — the balance never doubles.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, resumed.Outcome);
        Assert.True(resumed.IsReplay);

        harness.Finance.SaveChanges(); // flush any tracked state before the final read
        CreditBalance reread = harness.Finance.CreditBalances.AsNoTracking().Single();
        Assert.Equal(captured, reread.Balance.MinorUnits);

        Result<RentalRefundCaseSnapshot?> finalTimeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        // The replay appended nothing: still exactly the Approved + Posted pair.
        Assert.Equal(2, finalTimeline.Value!.Entries.Count);
    }

    [Fact]
    public void A_credit_replay_refuses_a_different_family_and_never_touches_the_balance()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.AppCredit, Utc(1, 10));
        harness.SaveBooking(booking);

        Assert.Equal(
            RefundOutcome.Committed,
            Run(harness, booking, caseId, captured, RentalRefundDestination.AppCredit,
                RentalRefundReason.CancellationBeforeDelivery, "key-1").Outcome);

        // M1 replay binding: replay validates case + family + amount + evidence —
        // never the amount alone. A different family (or any content change)
        // changes the proof token, so the seal refuses; the balance is untouched.
        Result<RentalRefundMovementSnapshot> redirected = harness.Recorder.PostRentalRefundCreditAsync(
            caseId,
            Guid.CreateVersion7(), // a stranger family
            Money.FromMinorUnits(captured),
            Guid.CreateVersion7(),
            Utc(2, 9),
            RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund,
                RentalRefundDestination.AppCredit, "key-1"),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(redirected.IsFailure);
        Assert.Equal("Finance.RentalRefund.IdempotencyConflict", redirected.Error!.Code);

        // Exactly one balance row exists and it still holds the original post.
        harness.Finance.SaveChanges();
        CreditBalance balance = harness.Finance.CreditBalances.AsNoTracking().Single();
        Assert.Equal(captured, balance.Balance.MinorUnits);
    }

    [Fact]
    public void A_timed_out_case_resolves_to_verified_success_after_reconciliation_and_the_resume_replays_it()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        // Crash window: approval stands alone.
        harness.Recorder.RecordTransferAsync(
            caseId, Money.FromMinorUnits(captured), RentalRefundReason.CancellationBeforeDelivery,
            booking.RequestedByUserId, Utc(1, 10), "key-1", null,
            RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund,
                RentalRefundDestination.OriginalPaymentMethod, "key-1"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(
            RefundOutcome.BlockedUnknown,
            Run(harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
                RentalRefundReason.CancellationBeforeDelivery, "key-1").Outcome);

        // Operations reconciles with authenticated evidence, then accounts the
        // verified success with the mandatory provider reference AND evidence.
        Result<RentalRefundMovementSnapshot> reconciled = harness.Recorder.ReconcileTimeoutAsync(
            caseId, Guid.CreateVersion7(), "ops/paymob-inquiry-2026-10-23/refunded", "prov-manual-1",
            Utc(2, 10), CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(reconciled.IsSuccess);

        Result<RentalRefundMovementSnapshot> completed = harness.Recorder.CompleteTransferAsync(
            caseId, Money.FromMinorUnits(captured), "prov-manual-1", Guid.CreateVersion7(),
            Utc(2, 11), "ops completion evidence", CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(completed.IsSuccess);

        // An EMPTY completion evidence is refused outright (M2: the seam guard,
        // not just the route).
        Result<RentalRefundMovementSnapshot> evidenceless = harness.Recorder.CompleteTransferAsync(
            caseId, Money.FromMinorUnits(captured), "prov-manual-1", Guid.CreateVersion7(),
            Utc(2, 11), "  ", CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(evidenceless.IsFailure);
        Assert.Equal("Finance.RentalRefund.Invalid", evidenceless.Error!.Code);

        // The family-facing resume finally replays the verified outcome — no resend.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, resumed.Outcome);
        Assert.True(resumed.IsReplay);
        Assert.Equal("prov-manual-1", resumed.GatewayReference);
        Assert.Equal(0, harness.Paymob.RefundCalls);
    }

    [Fact]
    public void A_timed_out_case_resolves_to_verified_nonpayment_and_releases_the_claim_for_a_fresh_retry()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(3);
        long captured = 3 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        harness.Recorder.RecordTransferAsync(
            caseId, Money.FromMinorUnits(captured), RentalRefundReason.CancellationBeforeDelivery,
            booking.RequestedByUserId, Utc(1, 10), "key-1", null,
            RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund,
                RentalRefundDestination.OriginalPaymentMethod, "key-1"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(
            RefundOutcome.BlockedUnknown,
            Run(harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
                RentalRefundReason.CancellationBeforeDelivery, "key-1").Outcome);

        // Reconciliation proves nonpayment; the accounting reverses the award.
        Result<RentalRefundMovementSnapshot> reconciled = harness.Recorder.ReconcileTimeoutAsync(
            caseId, Guid.CreateVersion7(), "ops/paymob-inquiry-2026-10-23/no-refund-found", "prov-inquiry-1",
            Utc(2, 10), CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(reconciled.IsSuccess);

        Result<RentalRefundMovementSnapshot> reversed = harness.Recorder.RecordFailureAsync(
            caseId, Money.FromMinorUnits(captured), Guid.CreateVersion7(), Utc(2, 11),
            "ops reversal evidence", CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(reversed.IsSuccess);
        // Frozen-domain limitation (interim truth): the reversal entry's provider
        // slot stays EMPTY — the failed evidence has no aggregate slot until the
        // queued frozen-domain amendment lands.
        Assert.Null(
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult().Value!
                .Entries.Last(entry => entry.EntryType == RefundLedgerEntryType.Failed).ProviderReference);

        // The resume classifies the verified failure: retryable, claim releasable.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.FailedRetryable, resumed.Outcome);

        booking.ReleaseRefundClaim(Utc(2, 12));
        harness.SaveBooking(booking);

        Guid freshCaseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, freshCaseId, RentalRefundDestination.OriginalPaymentMethod, Utc(2, 13));
        harness.SaveBooking(booking);

        harness.Paymob.RefundError = null;
        RefundExecutionResult retry = Run(
            harness, booking, freshCaseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-2");
        Assert.Equal(RefundOutcome.Committed, retry.Outcome);
        Assert.Equal(1, harness.Paymob.RefundCalls); // the dead case never re-sent; the retry is a fresh movement
    }

    [Fact]
    public void A_zero_failure_without_a_preceding_timeout_does_not_block_forever()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        Guid caseId = Guid.CreateVersion7();

        // A ZERO-award case: approval recorded, then a zero reversal — no timeout
        // anywhere. A zero reversal moved nothing: it must NOT be mistaken for a
        // reconciliation marker and brick the case.
        Assert.True(harness.Recorder.RecordTransferAsync(
            caseId, Money.FromMinorUnits(0), RentalRefundReason.CancellationBeforeDelivery,
            booking.RequestedByUserId, Utc(1, 10), "key-1", null,
            RentalRefundExecutor.ComposeEvidence(
                booking.Id, RentalRefundMovementKind.CancellationFullRefund,
                RentalRefundDestination.OriginalPaymentMethod, "key-1"),
            CancellationToken.None).GetAwaiter().GetResult().IsSuccess);

        Assert.True(harness.Recorder.RecordFailureAsync(
            caseId, Money.FromMinorUnits(0), Guid.CreateVersion7(), Utc(1, 11),
            "zero reversal", CancellationToken.None).GetAwaiter().GetResult().IsSuccess);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.Failed, timeline.Value!.LatestEntryType);
        Assert.Equal(0, timeline.Value.LatestAmountMinorUnits);
        Assert.DoesNotContain(timeline.Value.Entries, entry => entry.EntryType == RefundLedgerEntryType.TimedOut);

        // Zero reversal with no timeout ⇒ verified non-movement: release + retry.
        RefundExecutionResult resumed = Run(
            harness, booking, caseId, 0, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.FailedRetryable, resumed.Outcome);
    }

    [Fact]
    public void An_oversized_idempotency_key_is_refused_before_any_record_or_provider_call()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;
        Guid caseId = Guid.CreateVersion7();

        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        // One character past the DERIVED evidence budget — the handler refuses
        // first with a stable 400; the executor guard is the last line.
        string oversized = new('k', RentalRefundExecutor.MaximumIdempotencyKeyLength + 1);
        Assert.Equal(68, RentalRefundExecutor.MaximumIdempotencyKeyLength); // pinned derivation: (300−163)/2

        RefundExecutionResult refused = Run(
            harness, booking, caseId, captured, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, oversized);

        Assert.Equal(RefundOutcome.FailedRetryable, refused.Outcome);
        Assert.Equal("Rentals.Refund.IdempotencyKeyTooLong", refused.Failure!.Code);
        Assert.Equal(0, harness.Paymob.RefundCalls);

        Result<RentalRefundCaseSnapshot?> timeline =
            harness.Recorder.ReadCaseAsync(caseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Null(timeline.Value); // nothing was ever recorded in Finance
    }

    [Fact]
    public void A_zero_award_records_only_the_approval_and_replays_as_complete()
    {
        using var harness = new Harness();
        // An UNPAID booking derives a zero cancellation award (#31).
        RentalEquipment equipment = RentalEquipment.Create(
            $"rig-{Guid.NewGuid():N}", "جهاز اختبار", null, null, DailyRate, DepositAmount, 30, Utc(1, 8));
        harness.Rentals.RentalEquipment.Add(equipment);
        RentalBooking booking = RentalBooking.Create(
            equipment, Guid.CreateVersion7(), Guid.CreateVersion7(), 2, null, Utc(1, 8));
        harness.Rentals.RentalBookings.Add(booking);
        harness.Rentals.SaveChanges();

        Guid caseId = Guid.CreateVersion7();
        booking.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, 0, caseId, RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        harness.SaveBooking(booking);

        RefundExecutionResult first = Run(
            harness, booking, caseId, 0, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, first.Outcome);
        Assert.Equal(0, first.AwardedMinorUnits);
        Assert.Equal(0, harness.Paymob.RefundCalls); // no rails call for a zero award

        RefundExecutionResult resumed = Run(
            harness, booking, caseId, 0, RentalRefundDestination.OriginalPaymentMethod,
            RentalRefundReason.CancellationBeforeDelivery, "key-1");
        Assert.Equal(RefundOutcome.Committed, resumed.Outcome);
        Assert.True(resumed.IsReplay);
        Assert.Equal(0, harness.Paymob.RefundCalls);
    }

    [Fact]
    public void Two_claims_on_one_booking_cannot_both_commit_the_row()
    {
        using var harness = new Harness();
        RentalBooking booking = harness.SeedCapturedBooking(2);
        long captured = 2 * DailyRate + DepositAmount;

        // Two independent contexts over the same SQLite database, the way two
        // process instances would load the same row.
        var options = new DbContextOptionsBuilder<RentalsDbContext>().UseSqlite(harness.RentalsConnection).Options;
        using var contenderA = new RentalsDbContext(options);
        using var contenderB = new RentalsDbContext(options);

        RentalBooking loadedA = contenderA.RentalBookings.Single(candidate => candidate.Id == booking.Id);
        RentalBooking loadedB = contenderB.RentalBookings.Single(candidate => candidate.Id == booking.Id);

        loadedA.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 10));
        contenderA.SaveChanges(); // the first claim commits

        loadedB.BeginRefund(RentalRefundMovementKind.CancellationFullRefund, captured, Guid.CreateVersion7(), RentalRefundDestination.OriginalPaymentMethod, Utc(1, 11));
        // The booking's version token refuses the loser as a relational conflict —
        // a retryable error, never a second movement case.
        Assert.Throws<DbUpdateConcurrencyException>(() => contenderB.SaveChanges());
    }

    // ---------------------------------------------------------------------------------------------
    // M3 + B1 + input bounds: handler-level behaviour (Families InMemory mirrors the deploy split).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_repeated_cancel_of_an_unpaid_booking_replays_success_instead_of_a_conflict()
    {
        using var harness = new Harness();
        using FamiliesDbContext families = NewFamiliesDatabase("rentals-f4-unpaid-cancel");

        Family family = Family.Create(UserId.New(), "F4 unpaid cancel replay");
        families.Families.Add(family);
        families.SaveChanges();

        RentalEquipment equipment = RentalEquipment.Create(
            $"rig-{Guid.NewGuid():N}", "جهاز اختبار", null, null, DailyRate, DepositAmount, 30, Utc(1, 8));
        harness.Rentals.RentalEquipment.Add(equipment);
        RentalBooking booking = RentalBooking.Create(equipment, family.Id.Value, family.OwnerUserId.Value, 2, null, Utc(1, 8));
        harness.Rentals.RentalBookings.Add(booking);
        harness.Rentals.SaveChanges();

        var handler = new CancelRentalBookingCommandHandler(harness.Rentals, families, harness.Paymob, harness.Recorder);

        Result<RentalBookingRefundResponse> first = await handler.Handle(
            new CancelRentalBookingCommand(booking.Id, family.OwnerUserId.Value, RentalRefundDestination.OriginalPaymentMethod, null, Utc(1, 9)),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal("CancelledWithoutCapture", first.Value.Reason);
        Assert.False(first.Value.IsReplay);
        Assert.Equal(RentalBookingStatus.CancelledBeforeDelivery, first.Value.Status);
        Assert.Equal(0, harness.Paymob.RefundCalls); // nothing captured — no movement of any kind

        // The REPEAT cancel — even with different content — replays the same
        // stable success instead of surfacing a state conflict (B1/M3).
        Result<RentalBookingRefundResponse> second = await handler.Handle(
            new CancelRentalBookingCommand(booking.Id, family.OwnerUserId.Value, RentalRefundDestination.AppCredit, "different-key", Utc(1, 10)),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal("CancelledWithoutCapture", second.Value.Reason);
        Assert.True(second.Value.IsReplay);
        Assert.Equal(0, harness.Paymob.RefundCalls);
    }

    [Fact]
    public async Task An_oversized_idempotency_key_fails_before_any_claim_or_finance_record()
    {
        using var harness = new Harness();
        using FamiliesDbContext families = NewFamiliesDatabase("rentals-f4-key-bound");

        Family family = Family.Create(UserId.New(), "F4 evidence budget");
        families.Families.Add(family);
        families.SaveChanges();

        RentalBooking booking = SeedFamiliesCapturedBooking(harness, family, plannedDays: 3);

        var handler = new CancelRentalBookingCommandHandler(harness.Rentals, families, harness.Paymob, harness.Recorder);

        string oversized = new('k', RentalRefundExecutor.MaximumIdempotencyKeyLength + 1);
        Result<RentalBookingRefundResponse> refused = await handler.Handle(
            new CancelRentalBookingCommand(booking.Id, family.OwnerUserId.Value, RentalRefundDestination.OriginalPaymentMethod, oversized, Utc(1, 9)),
            CancellationToken.None);

        // Stable 400 before BeginRefund, before any Finance call, before the provider.
        Assert.True(refused.IsFailure);
        Assert.Equal("Rentals.Refund.IdempotencyKeyTooLong", refused.Error!.Code);
        Assert.Null(booking.PendingRefundKind); // no claim was ever minted
        Assert.Empty(harness.Finance.RefundLedgers);
        Assert.Equal(0, harness.Paymob.RefundCalls);
    }

    [Fact]
    public async Task A_paid_cancel_retry_after_a_verified_failure_starts_a_fresh_claim_at_the_standing_award()
    {
        using var harness = new Harness();
        using FamiliesDbContext families = NewFamiliesDatabase("rentals-f4-paid-cancel-retry");

        Family family = Family.Create(UserId.New(), "F4 paid cancel retry");
        families.Families.Add(family);
        families.SaveChanges();

        RentalBooking booking = SeedFamiliesCapturedBooking(harness, family, plannedDays: 3);
        long captured = 3 * DailyRate + DepositAmount;

        var handler = new CancelRentalBookingCommandHandler(harness.Rentals, families, harness.Paymob, harness.Recorder);

        // First cancel mints the funded claim; the provider refuses definitively;
        // the signed reversal nets the case to zero and the claim releases — the
        // booking itself stays cancelled (Mastermind's paid-cancel replay fix).
        harness.Paymob.RefundError = new Error("Paymob.RefundRejected", "The refund was refused by the provider.");
        Result<RentalBookingRefundResponse> failed = await handler.Handle(
            new CancelRentalBookingCommand(booking.Id, family.OwnerUserId.Value, RentalRefundDestination.OriginalPaymentMethod, "key-1", Utc(1, 9)),
            CancellationToken.None);
        Assert.True(failed.IsFailure);
        Assert.Equal("Paymob.RefundRejected", failed.Error!.Code);

        Guid deadCaseId = harness.Finance.RefundLedgers.Single().RefundCaseId;
        Assert.Equal(RentalBookingStatus.CancelledBeforeDelivery, booking.Status);
        Assert.Null(booking.PendingRefundKind); // the verified failure released the claim

        // The RETRY must NOT re-cancel and must NOT resume the dead case: it
        // starts a FRESH claim at the standing (policy-derived) award.
        harness.Paymob.RefundError = null;
        Result<RentalBookingRefundResponse> retry = await handler.Handle(
            new CancelRentalBookingCommand(booking.Id, family.OwnerUserId.Value, RentalRefundDestination.OriginalPaymentMethod, "key-2", Utc(1, 12)),
            CancellationToken.None);

        Assert.True(retry.IsSuccess);
        Assert.Equal("CancelledWithFullRefund", retry.Value.Reason);
        Assert.False(retry.Value.IsReplay);
        Assert.Equal(captured / 100m, retry.Value.Awarded);
        Guid retryCaseId = retry.Value.RefundCaseId;
        Assert.NotEqual(deadCaseId, retryCaseId);
        Assert.Equal(2, harness.Paymob.RefundCalls); // the refused first attempt + the retry

        // Two Finance cases now: the dead one (netted to zero) and the live one
        // (approval + confirmed transfer = the standing award completed).
        Assert.Equal(2, harness.Finance.RefundLedgers.Count());
        Assert.Equal(
            0,
            harness.Recorder.ReadCaseAsync(deadCaseId, CancellationToken.None).GetAwaiter().GetResult().Value!.NetRecordedMinorUnits);
        Result<RentalRefundCaseSnapshot?> liveCase =
            harness.Recorder.ReadCaseAsync(retryCaseId, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(RefundLedgerEntryType.TransferConfirmed, liveCase.Value!.LatestEntryType);
        Assert.Equal(captured, liveCase.Value.TotalCompletedMinorUnits);
    }
}
