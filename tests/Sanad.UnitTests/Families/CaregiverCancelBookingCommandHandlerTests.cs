using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Infrastructure.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Independent handler-level contract tests for <see cref="CaregiverCancelBookingCommandHandler"/> as
/// merged at <c>fbcb8cb1d5d3c49e874d1f65af9cc9b7018015e1</c> (caregiver confirmed-cancel + reject facts,
/// B1-B3). Authored, not executed: no build, test run or migration is performed here; the owner is the
/// only build machine.
/// <para>
/// Rules these tests hold the handler to, read from the merged source:
/// <list type="bullet">
/// <item><description>
/// The booking lookup is scoped to the acting caregiver; an unknown id and a booking of another
/// caregiver answer the identical <c>Bookings.NotFound</c> shape, so the response never reveals that a
/// foreign booking exists.
/// </description></item>
/// <item><description>
/// A caregiver may cancel only their own <see cref="BookingStatus.Confirmed"/> booking, before the
/// visit starts; an accepted booking requires a known reason category AND a non-blank note, and a
/// category may never be supplied before acceptance.
/// </description></item>
/// <item><description>
/// <see cref="BookingCancellationPolicy.Decide"/> runs on the untouched aggregate. The 60-minute grace
/// window belongs to the FAMILY side only: a caregiver cancellation is a full captured refund with
/// exactly one caregiver incident flag regardless of elapsed time since acceptance.
/// </description></item>
/// <item><description>
/// Refund entitlement comes only from the policy; a full-captured entitlement refunds the succeeded
/// transaction's captured amount as-is (fee included, nothing recomputed), and a refund gateway failure
/// never fails the cancellation.
/// </description></item>
/// <item><description>
/// Exactly one <see cref="BookingCancellationFact"/> is recorded through the production
/// <see cref="BookingCancellationFactRecorder"/>, and one <c>SaveChangesAsync</c> commits the booking
/// mutation and the fact together. A fact unique violation on save maps to
/// <c>Bookings.Cancel.AlreadyProcessed</c>; any other save failure is not swallowed.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Fixtures are built only through public application and domain entry points (checkout, payment intent,
/// the Paymob confirmation webhook, caregiver accept / start / complete); no reflection, no internals.
/// Stored state is always read through a fresh context over the same in-memory database. The helpers in
/// this class are private on purpose; they are local copies of the patterns in
/// <c>CancelBookingCommandHandlerTests</c> and are not shared across test classes.
/// </para>
/// </summary>
public sealed class CaregiverCancelBookingCommandHandlerTests
{
    private const decimal CaregiverBaseFee = 500m;

    private const int UndefinedReasonCategory = 999;

    /// <summary>Authoritative "now" for the cancellations under test; passed to the commands explicitly.</summary>
    private static readonly DateTime UtcNow = new(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Booking slot two hours after <see cref="UtcNow"/> (10:00 UTC), far from the cancellation times.</summary>
    private static readonly DateTime DistantStartUtc = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    // =============================================================================================
    // Negative-first: every return path names one exact error code and leaves no trace at all.
    // =============================================================================================

    [Fact]
    public async Task A1_UnknownBookingId_IsRejectedWithNotFound_AndChangesNothing()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            BookingId.New(),
            UtcNow,
            reason: "ghost booking");

        AssertFailedWith(result, "Bookings.NotFound");
        Assert.Equal("Booking not found for this caregiver.", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task A2_BookingOfAnotherCaregiver_IsRejectedWithTheIdenticalNotFoundShape_AndChangesNothing()
    {
        // Same shape as A1 on purpose: the response must not reveal that the booking exists.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            CaregiverId.New(),
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "foreign attempt");

        AssertFailedWith(result, "Bookings.NotFound");
        Assert.Equal("Booking not found for this caregiver.", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task A3_ConfirmedBookingWithoutReasonCategory_IsRejectedWithReasonCategoryRequired()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: null);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryRequired");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A4_ConfirmedBookingWithUndefinedReasonCategory999_IsRejectedWithReasonCategoryInvalid()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: UndefinedReasonCategory);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryInvalid");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A5_ConfirmedBookingWithWhitespaceReasonNote_IsRejectedWithReasonRequired()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "   ",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        AssertFailedWith(result, "Bookings.Cancel.ReasonRequired");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A6_ConfirmedBookingWithANoteLongerThanTheMaximum_FailsInTheFeedbackConstructor_AsDomainInvalidOperation()
    {
        // The over-length note is refused by BookingCancellationFeedback.Create, which throws a
        // DomainException before the policy runs and before any mutation; the handler's outer catch
        // surfaces it as Bookings.Domain.InvalidOperation.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        string overLongNote = new('x', Booking.MaximumReasonLength + 1);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: overLongNote,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Equal(
            $"Cancellation note cannot exceed {Booking.MaximumReasonLength} characters.",
            result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A7_PendingPaymentBookingWithoutCategory_IsRefusedByThePolicy_AsDomainInvalidOperation()
    {
        // The policy refuses a caregiver Cancel on any non-Confirmed booking ("A caregiver can only
        // cancel a booking they have already accepted."), and the handler maps that DomainException.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: null,
            reasonCategory: null);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Contains("already accepted", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public void A8_ConfirmedWithARecordedVisitStart_TheDefensivePolicyBranchRefusesTheCancellation()
    {
        // Pinned at the policy-input level because the aggregate itself can never hold this state:
        // StartVisit moves a Confirmed booking to InProgress in the same call, so "Confirmed with
        // StartedOnUtc recorded" is unreachable through the public transitions, and the handler can
        // only ever observe the started visit as the InProgress status (case A9) or a completed visit
        // as Completed (case A10) — both of which pin the handler's mapping of this refusal family to
        // Bookings.Domain.InvalidOperation. This test pins the defensive policy branch itself, which
        // the handler would route through the same DomainException catch.
        DateTime acceptedAtUtc = UtcNow.AddMinutes(-30);
        DateTime startedAtUtc = acceptedAtUtc.AddMinutes(30);
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "visit already underway");

        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.ForCaregiverCancellation(
            BookingStatus.Confirmed,
            acceptedAtUtc,
            startedAtUtc,
            UtcNow,
            BookingCaptureEvidence.Captured,
            feedback);

        DomainException error = Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));

        Assert.Contains("already recorded a visit start", error.Message);
    }

    [Fact]
    public async Task A9_InProgressBooking_IsRefusedByThePolicy_AsDomainInvalidOperation()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);
        booking = await StartVisitAsync(dbContext, booking, UtcNow);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "visit in progress",
            reasonCategory: null);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Contains("already started or completed", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.InProgress);
    }

    [Fact]
    public async Task A10_CompletedBooking_IsRefusedByThePolicy_AsDomainInvalidOperation()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);
        booking = await StartVisitAsync(dbContext, booking, UtcNow.AddMinutes(-5));
        booking = await CompleteVisitAsync(dbContext, booking, UtcNow);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "visit completed",
            reasonCategory: null);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Contains("already started or completed", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Completed);
    }

    [Fact]
    public async Task A11_AlreadyCancelledByCaregiverReCancelled_IsRefusedWithDomainInvalidOperation_AndNoSecondFact()
    {
        // The refund gateway is made to refuse, so the first cancellation ends the booking in
        // CancelledByCaregiver (not Refunded) — the exact already-ended state this case pins.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        Result first = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(first.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);
        Assert.Equal(
            BookingStatus.CancelledByCaregiver,
            (await PersistedBookingAsync(store, booking.Id)).Status);

        // No category on the second attempt: the booking is no longer Confirmed, and a supplied
        // category would take the pre-acceptance refusal branch (A12) instead of the policy.
        Result second = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow.AddMinutes(1),
            reason: "changed plans again",
            reasonCategory: null);

        // The policy refuses every already-ended state on the untouched aggregate, and the handler
        // turns that DomainException into a coded failure rather than letting it escape.
        AssertFailedWith(second, "Bookings.Domain.InvalidOperation");
        Assert.Contains("already ended", second.Error.Message);

        // A second short-lived context re-reads the store without reassigning the outer
        // using variable (C# forbids assigning to a using variable: CS1656).
        using FamiliesDbContext reread = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(reread, booking.Id);

        Assert.Equal(BookingStatus.CancelledByCaregiver, persisted.Status);
        await SingleFactAsync(reread);
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task A12_InProgressBookingWithAReasonCategory_IsRefusedWithReasonCategoryNotAllowedPreAcceptance()
    {
        // KNOWN, ACCEPTED INCONSISTENCY — pinned as-is, do not "correct" it:
        // the caregiver handler refuses a pre-acceptance category for EVERY non-Confirmed status
        // (status != Confirmed && ReasonCategory != null), so an InProgress booking answered here with
        // Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance (an unmapped code, i.e. a 400 shape)
        // BEFORE the policy runs. The family handler restricts the same check to
        // PendingPayment / PendingCaregiverApproval, so the same illegal state on the family side
        // falls through to the policy and answers Bookings.Domain.InvalidOperation (a 409). The two
        // endpoints therefore answer with different codes for the same illegal state. This is
        // deliberately left as-is and queued for the B1-C alignment; this test pins today's behavior.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);
        booking = await StartVisitAsync(dbContext, booking, UtcNow);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "visit in progress",
            reasonCategory: 1);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.InProgress);
    }

    [Fact]
    public async Task A13_FactUniqueViolationOnSave_MapsToAlreadyProcessed_AndPersistsNeitherFactNorCancellation()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            arrangeContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        // The InMemory provider never enforces unique indexes, so the parallel-cancel race is arranged
        // at the persistence seam instead: the handler's own save reports the unique violation that
        // PostgreSQL reports for ux_booking_cancellation_facts_booking.
        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var seam = new SaveSeamContext(handlerContext, FactUniqueViolationFailure());

        Result result = await CancelWithAsync(
            seam,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        AssertFailedWith(result, "Bookings.Cancel.AlreadyProcessed");

        // The fact reached the change tracker through the production recorder, and the blocked save
        // committed none of it.
        EntityEntry<BookingCancellationFact> trackedFact =
            Assert.Single(handlerContext.ChangeTracker.Entries<BookingCancellationFact>());
        Assert.Equal(EntityState.Added, trackedFact.State);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Confirmed, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        await AssertNoFactsAsync(store);

        // Order of operations as merged: the provider refund is attempted before the single save, and
        // a failed save does not undo a gateway call. Recorded here as observed behaviour for the
        // reviewer (same observation as the family handler's N10).
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task A14_SaveFailureThatIsNotTheFactUniqueViolation_PropagatesInsteadOfMappingToAlreadyProcessed()
    {
        // Proves the catch is filtered (DbUpdateException when IsFactUniqueViolation) and not a
        // blanket swallow: an unrelated persistence error escapes the handler untouched.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            arrangeContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var seam = new SaveSeamContext(handlerContext, UnrelatedPersistenceFailure());

        await Assert.ThrowsAsync<DbUpdateException>(() => CancelWithAsync(
            seam,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency));

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Confirmed, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        await AssertNoFactsAsync(store);

        // The refund was attempted before the single save (the merged order); nothing about that call
        // is rolled back by the failed save.
        Assert.Single(paymob.RefundCalls);
    }

    // =============================================================================================
    // Positive / behavioural: the recorded fact, the aggregate state and the provider call must all
    // agree with the policy branch that was taken.
    // =============================================================================================

    [Fact]
    public async Task A15_ConfirmedPaidCancelWithCategoryAndNote_RefundsTheFullCapturedAmountOnce_AndRecordsTheFlaggedFact()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        DateTime acceptedAtUtc = UtcNow.AddMinutes(-5);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        // 500 base + 15% platform fee, captured once at checkout pricing.
        Assert.Equal(575m, capture.Amount);

        const string note = "family emergency";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: note,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        // Exactly one provider call, for the succeeded transaction's id and its captured amount as
        // recorded — full captured amount, fee included, nothing recomputed here.
        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Equal(note, persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Equal(UtcNow, persisted.RefundedOnUtc);
        Assert.Equal(paymob.RefundTransactionId, persisted.PaymobRefundTransactionId);
        Assert.Equal(PaymentTransactionStatus.Refunded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, fact.RefundDecisionReason);
        Assert.True(fact.IsCaregiverIncident);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal(note, fact.ReasonNote);
    }

    [Fact]
    public async Task A16_CancelTenDaysAfterAcceptance_StillRefundsInFull_AndStillFlagsTheIncident()
    {
        // The 60-minute grace window belongs to the FAMILY side only. This is the guard that keeps
        // that family-only rule from leaking onto the caregiver: a caregiver cancelling ten days
        // after acceptance still gets a full captured refund and the incident flag.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        DateTime acceptedAtUtc = UtcNow;
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        DateTime lateCancelUtc = acceptedAtUtc.AddDays(10);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            lateCancelUtc,
            reason: "plans changed",
            reasonCategory: (int)BookingCancellationReasonCategory.Other);

        Assert.True(result.IsSuccess);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Equal(lateCancelUtc, persisted.CancelledOnUtc);
        Assert.Equal(lateCancelUtc, persisted.RefundedOnUtc);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, lateCancelUtc);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, fact.RefundDecisionReason);
        Assert.True(fact.IsCaregiverIncident);
        Assert.Equal(BookingCancellationReasonCategory.Other, fact.ReasonCategory);
    }

    [Fact]
    public async Task A17_RefundGatewayFailure_StillCancels_StaysCancelledByCaregiver_AndKeepsTheFlaggedFullRefundFact()
    {
        // Legacy tolerance: a refused refund never fails the cancellation. The booking stays
        // CancelledByCaregiver (not Refunded), and the fact still records what the policy owed.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };
        UserId caregiverUserId = UserId.New();

        DateTime acceptedAtUtc = UtcNow.AddMinutes(-5);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        const string note = "family emergency";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: note,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByCaregiver, persisted.Status);
        Assert.Equal(note, persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, fact.RefundDecisionReason);
        Assert.True(fact.IsCaregiverIncident);
    }

    [Fact]
    public async Task A18_PaidConfirmedCancel_WhenTheSucceededTransactionHasNoProviderId_SkipsTheProviderCallButStillCancelsAndRecordsTheFact()
    {
        // A succeeded transaction is capture evidence on its own (BookingCancellationPolicyInput
        // .ResolveCaptureEvidence), so the policy owes the full captured refund; but the handler's
        // refund lookup requires a provider transaction id, and there is none. No provider call, no
        // MarkRefunded — the booking stays CancelledByCaregiver — while the fact still records the
        // entitlement. This documents the already-known capture-reference gap (same class as the
        // family-side P1b); it is deliberately not fixed here.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        // The public MarkAsPaid accepts a missing provider reference; the Paymob webhook never
        // produces this shape, so it is arranged directly on the aggregate.
        const string orderId = "test-order-without-provider-reference";
        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, UtcNow.AddMinutes(-25));
        await dbContext.SaveChangesAsync();
        booking.MarkAsPaid(orderId, paymobTransactionId: null!, utcNow: UtcNow.AddMinutes(-20));
        await dbContext.SaveChangesAsync();

        var acceptHandler = new CaregiverAcceptBookingCommandHandler(dbContext);
        Result accepted = await acceptHandler.Handle(
            new CaregiverAcceptBookingCommand(booking.CaregiverId, booking.Id, UtcNow.AddMinutes(-10)),
            CancellationToken.None);
        Assert.True(accepted.IsSuccess);

        booking = await PersistedBookingAsync(dbContext, booking.Id);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);
        Assert.Null(capture.PaymobTransactionId);
        Assert.Equal(
            BookingCaptureEvidence.Captured,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        Assert.Empty(paymob.RefundCalls);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByCaregiver, persisted.Status);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, fact.RefundDecisionReason);
        Assert.True(fact.IsCaregiverIncident);
    }

    [Fact]
    public async Task A19_TheNoteIsWrittenTrimmedToTheAggregate_AndTheCategoryIsExactlyTheOneSupplied()
    {
        // The aggregate stores the trimmed note; the fact carries exactly the supplied category. A
        // category is never defaulted or invented when absent — the absent-category half of that rule
        // is pinned in CaregiverDeclineFactTests (B1/B2), because a caregiver cancel that succeeds
        // must by validation carry a known category.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        const string rawNote = "  running late  ";
        const string expectedNote = "running late";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: rawNote,
            reasonCategory: (int)BookingCancellationReasonCategory.TransportationIssues);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(expectedNote, persisted.CancellationReason);

        BookingCancellationFact fact = await SingleFactAsync(store);

        Assert.Equal(BookingCancellationReasonCategory.TransportationIssues, fact.ReasonCategory);
        Assert.Equal(expectedNote, fact.ReasonNote);
    }

    [Fact]
    public async Task A20_SuccessfulCancel_SavesExactlyOnce_AndASecondSequentialCancelIsRefusedWithoutAddingAFact()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        var seam = new SaveSeamContext(dbContext);

        Result result = await CancelWithAsync(
            seam,
            new BookingCancellationFactRecorder(dbContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        // The booking mutation and the recorded fact commit in the handler's single SaveChangesAsync.
        Assert.Equal(1, seam.SaveCalls);

        // A second, sequential cancel hits the already-ended (Refunded) booking: the policy refuses
        // it with the coded domain failure, and nothing is saved again. No category is supplied —
        // the booking is no longer Confirmed, so a category would take the A12 branch instead.
        Result second = await CancelWithAsync(
            seam,
            new BookingCancellationFactRecorder(dbContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow.AddMinutes(1),
            reason: "changed plans again",
            reasonCategory: null);

        AssertFailedWith(second, "Bookings.Domain.InvalidOperation");
        Assert.Equal(1, seam.SaveCalls);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Assert.Equal(BookingStatus.Refunded, (await PersistedBookingAsync(store, booking.Id)).Status);
        await SingleFactAsync(store);
    }

    [Fact]
    public async Task A21_IncidentFlag_SurvivesTheRefundStatusChange_WhenTheFactIsReRead()
    {
        // A successful caregiver cancel ends the booking Refunded in the same save. The fact is
        // append-only history: re-reading it after the refund status change, the incident flag is
        // still true — a refund must never erase history.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        DateTime acceptedAtUtc = UtcNow.AddMinutes(-5);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);

        const string note = "family emergency";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            UtcNow,
            reason: note,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);
        Assert.Equal(BookingStatus.Refunded, persisted.Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        Assert.True(fact.IsCaregiverIncident);
        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, fact.RefundDecisionReason);
    }

    // =============================================================================================
    // Optional endpoint-metadata pin (no HTTP call, no server start).
    // =============================================================================================

    [Fact]
    public void R1_EndpointMetadata_CaregiverAccessPolicyOnTheController_AndHttpPostCancelRoute()
    {
        // The owner's rules require the endpoint to sit behind the caregiver policy, with the cancel
        // action exposed as POST {bookingId:guid}/cancel. Verified on the attribute metadata alone.
        AuthorizeAttribute? authorize = typeof(CaregiverBookingsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .FirstOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CaregiverAccess, authorize!.Policy);

        MethodInfo? cancelAction = typeof(CaregiverBookingsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "CancelBooking");

        Assert.NotNull(cancelAction);

        HttpPostAttribute? httpPost = cancelAction!.GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(httpPost);
        Assert.Equal("{bookingId:guid}/cancel", httpPost!.Template);
    }

    // =============================================================================================
    // Fixtures and doubles.
    // =============================================================================================

    private static string NewDatabaseName() => $"sanad-caregiver-cancel-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (Family Family, Elderly Elderly) SeedFamily(FamiliesDbContext dbContext)
    {
        var family = Family.Create(UserId.New(), "Caregiver Cancellation Contract Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (family, elderly);
    }

    private static async Task<Booking> CheckoutAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly,
        DateTime createdAtUtc,
        DateTime scheduledStartUtc)
    {
        var handler = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FixedPricing(CaregiverBaseFee));

        TimeOnly startTime = TimeOnly.FromDateTime(scheduledStartUtc);

        Result<BookingCheckoutResponse> result = await handler.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(scheduledStartUtc),
                startTime,
                startTime.AddHours(2),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(createdAtUtc),
                createdAtUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var bookingId = new BookingId(result.Value.BookingId);

        return await PersistedBookingAsync(dbContext, bookingId);
    }

    /// <summary>
    /// Settles the booking through the real payment application path (intent, then the Paymob
    /// confirmation webhook), so the aggregate carries a Succeeded transaction with a provider reference.
    /// </summary>
    private static async Task<Booking> PayAsync(
        FamiliesDbContext dbContext,
        Booking booking,
        RecordingPaymobClient paymob,
        DateTime paidAtUtc,
        long paymobTransactionId)
    {
        var intentHandler = new CreateBookingPaymentIntentCommandHandler(dbContext, paymob);

        Result<BookingPaymentIntentResponse> intent = await intentHandler.Handle(
            new CreateBookingPaymentIntentCommand(
                booking.Id,
                booking.CreatedByUserId,
                PaymentMethod.Card,
                new PaymobBillingData("Salma", "Hassan", "salma.hassan@example.com", "+201234567890"),
                paidAtUtc),
            CancellationToken.None);

        Assert.True(intent.IsSuccess);

        // The webhook compares the reported amount against the recorded intent, in cents.
        long amountCents = (long)decimal.Round(
            booking.PriceSnapshot.TotalPayableAmount * 100m, 0, MidpointRounding.ToEven);

        var confirmHandler = new ConfirmBookingPaymentCommandHandler(dbContext, paymob);

        Result<ConfirmBookingPaymentResponse> confirmation = await confirmHandler.Handle(
            new ConfirmBookingPaymentCommand(
                intent.Value.PaymobOrderId,
                paymobTransactionId,
                amountCents,
                Success: true,
                Pending: false,
                UtcNow: paidAtUtc),
            CancellationToken.None);

        Assert.True(confirmation.IsSuccess);
        Assert.Equal("Paid", confirmation.Value.Outcome);

        Booking paid = await PersistedBookingAsync(dbContext, booking.Id);
        PaymentTransaction transaction = Assert.Single(paid.PaymentTransactions);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, paid.Status);
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal(paymobTransactionId.ToString(), transaction.PaymobTransactionId);

        return paid;
    }

    /// <summary>
    /// Paid and accepted booking (Confirmed), reached through the checkout, webhook and caregiver-accept
    /// application paths. <paramref name="acceptedAtUtc"/> is the acceptance time the policy would
    /// measure elapsed time from (the family grace window only; the caregiver branch never does).
    /// </summary>
    private static async Task<Booking> ConfirmedBookingAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly,
        RecordingPaymobClient paymob,
        DateTime acceptedAtUtc,
        DateTime scheduledStartUtc,
        long paymobTransactionId = 900500001)
    {
        Booking booking = await CheckoutAsync(
            dbContext, family, elderly, acceptedAtUtc.AddMinutes(-25), scheduledStartUtc);

        booking = await PayAsync(dbContext, booking, paymob, acceptedAtUtc.AddMinutes(-5), paymobTransactionId);

        var acceptHandler = new CaregiverAcceptBookingCommandHandler(dbContext);

        Result accepted = await acceptHandler.Handle(
            new CaregiverAcceptBookingCommand(booking.CaregiverId, booking.Id, acceptedAtUtc),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);

        Booking confirmed = await PersistedBookingAsync(dbContext, booking.Id);

        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        Assert.Equal(acceptedAtUtc, confirmed.ConfirmedOnUtc);

        return confirmed;
    }

    private static async Task<Booking> StartVisitAsync(
        FamiliesDbContext dbContext,
        Booking booking,
        DateTime startedAtUtc)
    {
        var handler = new CaregiverStartBookingCommandHandler(dbContext);

        Result result = await handler.Handle(
            new CaregiverStartBookingCommand(booking.CaregiverId, booking.Id, startedAtUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Booking started = await PersistedBookingAsync(dbContext, booking.Id);
        Assert.Equal(BookingStatus.InProgress, started.Status);

        return started;
    }

    private static async Task<Booking> CompleteVisitAsync(
        FamiliesDbContext dbContext,
        Booking booking,
        DateTime completedAtUtc)
    {
        var handler = new CaregiverCompleteBookingCommandHandler(dbContext);

        Result result = await handler.Handle(
            new CaregiverCompleteBookingCommand(booking.CaregiverId, booking.Id, null, completedAtUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Booking completed = await PersistedBookingAsync(dbContext, booking.Id);
        Assert.Equal(BookingStatus.Completed, completed.Status);

        return completed;
    }

    private static Task<Result> CancelAsync(
        FamiliesDbContext dbContext,
        RecordingPaymobClient paymob,
        CaregiverId caregiverId,
        UserId actorUserId,
        BookingId bookingId,
        DateTime utcNow,
        string? reason = null,
        int? reasonCategory = null) =>
        CancelWithAsync(
            dbContext,
            new BookingCancellationFactRecorder(dbContext),
            paymob,
            caregiverId,
            actorUserId,
            bookingId,
            utcNow,
            reason,
            reasonCategory);

    private static Task<Result> CancelWithAsync(
        IFamiliesDbContext dbContext,
        IBookingCancellationFactRecorder cancellationFactRecorder,
        RecordingPaymobClient paymob,
        CaregiverId caregiverId,
        UserId actorUserId,
        BookingId bookingId,
        DateTime utcNow,
        string? reason = null,
        int? reasonCategory = null) =>
        new CaregiverCancelBookingCommandHandler(dbContext, paymob, cancellationFactRecorder)
            .Handle(
                new CaregiverCancelBookingCommand(caregiverId, actorUserId, bookingId, reason, reasonCategory, utcNow),
                CancellationToken.None);

    private static async Task<Booking> PersistedBookingAsync(FamiliesDbContext dbContext, BookingId bookingId) =>
        await dbContext.Bookings.SingleAsync(b => b.Id == bookingId);

    private static async Task<BookingCancellationFact> SingleFactAsync(FamiliesDbContext dbContext)
    {
        List<BookingCancellationFact> facts = await dbContext.BookingCancellationFacts.ToListAsync();

        Assert.Single(facts);

        return facts[0];
    }

    private static async Task AssertNoFactsAsync(FamiliesDbContext dbContext) =>
        Assert.Empty(await dbContext.BookingCancellationFacts.ToListAsync());

    private static void AssertFailedWith(Result result, string expectedErrorCode)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedErrorCode, result.Error.Code);
    }

    /// <summary>
    /// The whole negative-first side effect contract: no fact row, the booking exactly as arranged,
    /// and the gateway never contacted by the handler under test.
    /// </summary>
    private static async Task AssertStoreUntouchedAsync(
        string databaseName,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        BookingStatus expectedStatus)
    {
        using FamiliesDbContext store = CreateDbContext(databaseName);

        await AssertNoFactsAsync(store);

        Booking persisted = await PersistedBookingAsync(store, bookingId);

        Assert.Equal(expectedStatus, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Empty(paymob.RefundCalls);
    }

    /// <summary>
    /// Fields every caregiver-confirmed-cancel fact carries, whatever the details: caregiver side,
    /// cancel action, the authorized actor, the caller's cancellation time, the current policy revision
    /// and exactly one caregiver incident flag (a caregiver cancelling a booking they accepted is
    /// always an incident, by policy).
    /// </summary>
    private static void AssertFactSpine(
        BookingCancellationFact fact,
        BookingId bookingId,
        UserId actorUserId,
        DateTime cancelledOnUtc)
    {
        Assert.Equal(bookingId, fact.BookingId);
        Assert.Equal(actorUserId, fact.ActorUserId);
        Assert.Equal(BookingCancellationActorSide.Caregiver, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, fact.Action);
        Assert.Equal(cancelledOnUtc, fact.CancelledOnUtc);
        Assert.Equal(BookingCancellationPolicy.CurrentPolicyVersion, fact.PolicyVersion);
        Assert.True(fact.IsCaregiverIncident);
    }

    /// <summary>
    /// What PostgreSQL reports for the one-fact-per-booking unique index, wrapped the way the provider
    /// surfaces it: a <see cref="DbUpdateException"/> whose inner chain carries the constraint name.
    /// </summary>
    private static DbUpdateException FactUniqueViolationFailure()
    {
        const string constraintName = "ux_booking_cancellation_facts_booking";

        var providerError = new InvalidOperationException(
            $"23505: duplicate key value violates unique constraint \"{constraintName}\"");

        return new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new Exception("See the inner exception for the provider error.", providerError));
    }

    private static DbUpdateException UnrelatedPersistenceFailure() =>
        new(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException(
                "23503: insert or update on table \"booking_cancellation_facts\" violates foreign key "
                + "constraint \"fk_booking_cancellation_facts_bookings_booking_id\""));

    /// <summary>
    /// The persistence seam the handler depends on, with an optionally failing save and a save counter.
    /// A subclass of <see cref="FamiliesDbContext"/> is not available — the production context is sealed —
    /// so the save is intercepted at the interface the handler actually consumes; reads are served by
    /// the real context, which is also the context the production recorder tracks the fact on.
    /// </summary>
    private sealed class SaveSeamContext(FamiliesDbContext inner, Exception? saveFailure = null) : IFamiliesDbContext
    {
        public int SaveCalls { get; private set; }

        public DbSet<Family> Families => inner.Families;

        public DbSet<Elderly> Elderlies => inner.Elderlies;

        public DbSet<FamilyInvitation> Invitations => inner.Invitations;

        public DbSet<Booking> Bookings => inner.Bookings;

        public DbSet<AssessmentQuestion> AssessmentQuestions => inner.AssessmentQuestions;

        public DbSet<AssessmentTier> AssessmentTiers => inner.AssessmentTiers;

        public DbSet<CareAssessment> CareAssessments => inner.CareAssessments;

        public DbSet<Medication> Medications => inner.Medications;

        public DbSet<MedicationDoseLog> MedicationDoseLogs => inner.MedicationDoseLogs;

        public DbSet<ElderlyNote> ElderlyNotes => inner.ElderlyNotes;

        public DbSet<ElderlyActivityLog> ElderlyActivityLogs => inner.ElderlyActivityLogs;
        public DbSet<BookingCancellationFact> BookingCancellationFacts => inner.BookingCancellationFacts;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;

            return saveFailure is null
                ? inner.SaveChangesAsync(cancellationToken)
                : Task.FromException<int>(saveFailure);
        }
    }

    private sealed class FixedPricing(decimal baseFee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(BookingCaregiverType.Medical, baseFee)));
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    /// <summary>
    /// Payment gateway double: every refund attempt is recorded with its transaction reference and
    /// amount, and the outcome is configurable so a refused refund can be arranged.
    /// </summary>
    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private readonly List<RefundCall> _refundCalls = [];

        public IReadOnlyList<RefundCall> RefundCalls => _refundCalls;

        public bool RefundSucceeds { get; init; } = true;

        public string RefundTransactionId { get; init; } = "test-refund-transaction";

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.BookingId.Value.ToString(),
                    "test-intention",
                    "test-client-secret",
                    "test-public-key")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            _refundCalls.Add(new RefundCall(paymobTransactionId, amount));

            return Task.FromResult(RefundSucceeds
                ? Result<string?>.Success(RefundTransactionId)
                : Result<string?>.Failure(
                    new Error("Paymob.RefundFailed", "The gateway refused the refund.")));
        }
    }
}
