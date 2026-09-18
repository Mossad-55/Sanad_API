using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
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
/// Independent handler-level contract tests for <see cref="CancelBookingCommandHandler"/> as merged at
/// <c>c651e33bdc5ddf834dce0fdbf3534dfd20ae1a24</c> (family booking cancellation activation, B1-B2).
/// <para>
/// Rules these tests hold the handler to, read from the merged source:
/// <list type="bullet">
/// <item><description>
/// Family resolution by membership, then the Owner/Editor role gate, then the booking lookup and its
/// family check — all before any aggregate mutation.
/// </description></item>
/// <item><description>
/// Feedback shape validation before the policy: an accepted (<see cref="BookingStatus.Confirmed"/>)
/// booking needs a defined category plus a non-blank note; a pre-acceptance booking may carry only an
/// optional note and never a category.
/// </description></item>
/// <item><description>
/// <see cref="BookingCancellationPolicy.Decide"/> runs on the untouched aggregate, and
/// <see cref="BookingCancellationPolicy.FamilyCancellationGraceWindow"/> is measured from the persisted
/// acceptance time (<see cref="Booking.ConfirmedOnUtc"/>) against the cancellation time the caller
/// supplies. The scheduled slot is not part of
/// <see cref="BookingCancellationPolicyInput"/> at all, so it cannot influence a refund.
/// </description></item>
/// <item><description>
/// Exactly one <see cref="BookingCancellationFact"/> is recorded through the production
/// <see cref="BookingCancellationFactRecorder"/>, and one <c>SaveChangesAsync</c> commits the booking
/// mutation and the fact together.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Fixtures are built only through public application and domain entry points (checkout, payment intent,
/// the Paymob confirmation webhook, caregiver acceptance); no reflection, no internals. Stored state is
/// always read through a fresh context over the same in-memory database, never through the change tracker
/// of a context that already mutated the aggregate in memory.
/// </para>
/// </summary>
public sealed class CancelBookingCommandHandlerTests
{
    private const decimal CaregiverBaseFee = 500m;

    private const int UndefinedReasonCategory = 999;

    /// <summary>Authoritative "now" for the cancellation under test; passed to the command explicitly.</summary>
    private static readonly DateTime UtcNow = new(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Booking slot two hours after <see cref="UtcNow"/> (10:00 UTC).</summary>
    private static readonly DateTime DistantStartUtc = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Booking slot thirty minutes after <see cref="UtcNow"/> (08:30 UTC).</summary>
    private static readonly DateTime NearStartUtc = new(2026, 9, 18, 8, 30, 0, DateTimeKind.Utc);

    // =============================================================================================
    // Negative-first: every return path names one exact error code and leaves no trace at all.
    // =============================================================================================

    [Fact]
    public async Task N1_ViewerMemberCancelling_IsRejectedWithUnauthorizedRole_AndChangesNothing()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        UserId viewerUserId = AddMember(dbContext, family, FamilyRole.Viewer);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            viewerUserId,
            UtcNow,
            reason: "viewer attempt");

        AssertFailedWith(result, "Bookings.UnauthorizedRole");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task N2_UserWithoutAnyFamily_IsRejectedWithFamilyNotFound_AndChangesNothing()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        UserId strangerUserId = UserId.New();

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            strangerUserId,
            UtcNow,
            reason: "no family at all");

        AssertFailedWith(result, "Bookings.FamilyNotFound");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task N3_UnknownBookingId_IsRejectedWithNotFound_AndRecordsNoFact()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            BookingId.New(),
            family.OwnerUserId,
            UtcNow,
            reason: "ghost booking");

        AssertFailedWith(result, "Bookings.NotFound");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task N4_BookingOfAnotherFamily_IsRejectedWithBookingNotInFamily_AndChangesNothing()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (homeFamily, homeElderly) = SeedFamily(dbContext);
        var (otherFamily, _) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, homeFamily, homeElderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            otherFamily.OwnerUserId,
            UtcNow,
            reason: "cross-family attempt");

        AssertFailedWith(result, "Bookings.BookingNotInFamily");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task N5_ConfirmedBookingWithoutReasonCategory_IsRejectedWithReasonCategoryRequired()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "family emergency",
            reasonCategory: null);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryRequired");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task N6_ConfirmedBookingWithUndefinedReasonCategory999_IsRejectedWithReasonCategoryInvalid()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "family emergency",
            reasonCategory: UndefinedReasonCategory);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryInvalid");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task N7_ConfirmedBookingWithWhitespaceReasonNote_IsRejectedWithReasonRequired()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "   ",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        AssertFailedWith(result, "Bookings.Cancel.ReasonRequired");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task N8_PendingPaymentBookingWithReasonCategory_IsRejectedWithReasonCategoryNotAllowedPreAcceptance()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: null,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        AssertFailedWith(result, "Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance");
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingPayment);
    }

    [Fact]
    public async Task N9_AlreadyCancelledBookingReCancelled_IsRejectedWithDomainInvalidOperation_AndNoSecondFact()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        Result first = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "changed plans");

        Assert.True(first.IsSuccess);

        Result second = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow.AddMinutes(1),
            reason: "changed plans");

        // The policy refuses every already-ended state on the untouched aggregate, and the handler turns
        // that DomainException into a coded failure rather than letting it escape.
        AssertFailedWith(second, "Bookings.Domain.InvalidOperation");

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, persisted.Status);
        Assert.Equal("changed plans", persisted.CancellationReason);
        await SingleFactAsync(store);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task N10_FactUniqueViolationOnSave_MapsToAlreadyProcessed409_AndPersistsNeitherFactNorCancellation()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await ConfirmedBookingAsync(
            arrangeContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        // The InMemory provider never enforces unique indexes, so the parallel-cancel race is arranged at
        // the persistence seam instead: the handler's own save reports the unique violation that PostgreSQL
        // reports for ux_booking_cancellation_facts_booking.
        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var failingContext = new SaveThrowingContext(handlerContext, FactUniqueViolationFailure());

        Result result = await CancelWithAsync(
            failingContext,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.Id,
            family.OwnerUserId,
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

        await AssertNoFactsAsync(store);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Confirmed, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);

        // Order of operations as merged: the provider refund is attempted before the single save, and a
        // failed save does not undo a gateway call. Recorded here as observed behaviour for the reviewer.
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task N10b_SaveFailureThatIsNotTheFactUniqueViolation_PropagatesInsteadOfMappingToAlreadyProcessed()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(arrangeContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var failingContext = new SaveThrowingContext(handlerContext, UnrelatedPersistenceFailure());

        await Assert.ThrowsAsync<DbUpdateException>(() => CancelWithAsync(
            failingContext,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "changed plans"));

        using FamiliesDbContext store = CreateDbContext(databaseName);

        await AssertNoFactsAsync(store);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.PendingPayment, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Empty(paymob.RefundCalls);
    }

    // =============================================================================================
    // Positive / behavioural: the recorded fact, the aggregate state and the provider call must all
    // agree with the policy branch that was taken.
    // =============================================================================================

    [Fact]
    public async Task P1_OwnerCancelsPendingApprovalWithoutNote_RecordsNullReasonAndRefundsTheCapturedAmount()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-20), paymobTransactionId: 900100001);

        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: null);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        // Before acceptance there is no mandatory note, so an empty reason stays empty on the aggregate.
        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Null(persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Equal(paymob.RefundTransactionId, persisted.PaymobRefundTransactionId);
        Assert.Equal(PaymentTransactionStatus.Refunded, Assert.Single(persisted.PaymentTransactions).Status);

        // Policy: PendingCaregiverApproval + family cancellation is a full captured refund, so the
        // gateway is asked once for exactly the captured amount.
        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, family.OwnerUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Null(fact.ConfirmedOnUtcUsed);
        Assert.Null(fact.ReasonCategory);
        Assert.Null(fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P1b_PendingApprovalCancellation_WhenCaptureHasNoProviderReference_SkipsTheProviderCallWhileRecordingFullRefundEntitlement()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        // A succeeded transaction is capture evidence on its own (BookingCancellationPolicyInput
        // .ResolveCaptureEvidence), but the aggregate's public MarkAsPaid accepts a missing provider
        // reference. The Paymob webhook never produces this shape; it is arranged here so the handler's
        // refund lookup and its recorded entitlement can be observed side by side.
        const string orderId = "test-order-without-provider-reference";

        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, UtcNow.AddMinutes(-25));
        await dbContext.SaveChangesAsync();

        booking.MarkAsPaid(orderId, paymobTransactionId: null!, utcNow: UtcNow.AddMinutes(-20));
        await dbContext.SaveChangesAsync();

        Assert.Equal(BookingStatus.PendingCaregiverApproval, booking.Status);
        Assert.Equal(
            BookingCaptureEvidence.Captured,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: null);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, persisted.Status);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);
        Assert.Empty(paymob.RefundCalls);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, family.OwnerUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Null(fact.ReasonCategory);
        Assert.Null(fact.ReasonNote);

        // The policy still says this family is owed the captured amount, and that is what the durable
        // fact records — while no provider call was made for it.
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P2_EditorCancelsPendingPaymentWithNote_StoresTheTrimmedNoteAsTheOnlyReason()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        UserId editorUserId = AddMember(dbContext, family, FamilyRole.Editor);

        const string rawNote = "  needs to reschedule  ";
        const string expectedNote = "needs to reschedule";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            editorUserId,
            UtcNow,
            reason: rawNote);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, persisted.Status);
        Assert.Equal(expectedNote, persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Empty(paymob.RefundCalls);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, editorUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingPayment, fact.StatusAtCancellation);
        Assert.Null(fact.ConfirmedOnUtcUsed);
        Assert.Null(fact.ReasonCategory);
        Assert.Equal(expectedNote, fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.NoRefundDue, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P3_OwnerCancelsConfirmedWithinTheGraceWindow_RefundsTheFullCapturedAmountOnceAndRecordsTheReason()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        // Five minutes after acceptance: strictly inside the 60-minute family grace window, so the policy
        // owes a full captured refund (Confirmed + family + elapsed time under the window).
        DateTime acceptedAtUtc = UtcNow.AddMinutes(-5);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        const string note = "family emergency";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: note,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        // The slot is two hours away. The policy measures the window from the acceptance time and never
        // reads the slot, so this distance is deliberately not what buys the refund below.
        Assert.Equal(TimeSpan.FromHours(2), ScheduledStart(booking) - UtcNow);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Equal(note, persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Equal(paymob.RefundTransactionId, persisted.PaymobRefundTransactionId);
        Assert.Equal(UtcNow, persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Refunded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, family.OwnerUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal(note, fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P4_OwnerCancelsConfirmedAtExactlySixtyMinutesFromAcceptance_KeepsTheMoneyAndStaysCancelledByFamily()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        // Exactly 60 minutes of elapsed time closes the family grace window, so the policy returns
        // NoRefundDue with the outside-window reason — a denial, never a "nothing captured" report.
        DateTime acceptedAtUtc = UtcNow.AddMinutes(-60);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, NearStartUtc);

        // The visit starts in half an hour, which is not a fact the policy receives.
        Assert.Equal(TimeSpan.FromMinutes(30), ScheduledStart(booking) - UtcNow);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        Assert.Empty(paymob.RefundCalls);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, persisted.Status);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, family.OwnerUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingRefundEntitlement.NoRefundDue, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow, fact.RefundDecisionReason);
        Assert.NotEqual(BookingRefundDecisionReason.NothingCaptured, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P5_OwnerCancelsConfirmedWithinTheGraceWindow_WhenTheGatewayRefundFails_StillCancelsAndRecordsTheFact()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };

        DateTime acceptedAtUtc = UtcNow.AddMinutes(-5);
        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, acceptedAtUtc, DistantStartUtc);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        const string note = "family emergency";

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: note,
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        // Legacy tolerance: a refused refund never fails the cancellation.
        Assert.True(result.IsSuccess);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, persisted.Status);
        Assert.Equal(note, persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        // The recorded entitlement is the policy's answer, not the gateway's outcome: the booking was
        // owed the refund, the provider refused it, and the fact still says what was owed.
        AssertFactSpine(fact, booking.Id, family.OwnerUserId, UtcNow);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(acceptedAtUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal(note, fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task P6_SuccessfulCancellation_PersistsBookingAndFactFromTheSingleSave_AndASecondSaveAddsNothing()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-5), DistantStartUtc);

        Result result = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(result.IsSuccess);

        // The handler's single SaveChangesAsync committed the booking mutation and the recorded fact: the
        // same context sees both.
        Assert.Equal(BookingStatus.Refunded, (await PersistedBookingAsync(dbContext, booking.Id)).Status);
        Assert.Single(await dbContext.BookingCancellationFacts.ToListAsync());

        await dbContext.SaveChangesAsync();

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Assert.Single(await store.BookingCancellationFacts.ToListAsync());
        Assert.Equal(BookingStatus.Refunded, (await PersistedBookingAsync(store, booking.Id)).Status);
    }

    [Fact]
    public async Task P7_ReCancellingAfterASuccessfulCancellation_ReturnsDomainInvalidOperationWithoutEscapingTheHandler()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-20), paymobTransactionId: 900700001);

        Result first = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow,
            reason: null);

        Assert.True(first.IsSuccess);
        Assert.Single(paymob.RefundCalls);

        Result second = await CancelAsync(
            dbContext,
            paymob,
            booking.Id,
            family.OwnerUserId,
            UtcNow);

        AssertFailedWith(second, "Bookings.Domain.InvalidOperation");

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Single(paymob.RefundCalls);
        await SingleFactAsync(store);
    }

    // =============================================================================================
    // Fixtures and doubles.
    // =============================================================================================

    private static string NewDatabaseName() => $"sanad-cancel-handler-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (Family Family, Elderly Elderly) SeedFamily(FamiliesDbContext dbContext)
    {
        var family = Family.Create(UserId.New(), "Cancellation Contract Family");

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

    private static UserId AddMember(FamiliesDbContext dbContext, Family family, FamilyRole role)
    {
        var memberUserId = UserId.New();

        family.AddMember(
            FamilyMember.Create(
                memberUserId,
                family.OwnerUserId,
                FamilyRelationshipType.Other,
                role));

        dbContext.SaveChanges();

        return memberUserId;
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
    /// application paths. <paramref name="acceptedAtUtc"/> is the acceptance time the policy measures its
    /// 60-minute family grace window from.
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

    private static Task<Result> CancelAsync(
        FamiliesDbContext dbContext,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        UserId actorUserId,
        DateTime utcNow,
        string? reason = null,
        int? reasonCategory = null) =>
        CancelWithAsync(
            dbContext,
            new BookingCancellationFactRecorder(dbContext),
            paymob,
            bookingId,
            actorUserId,
            utcNow,
            reason,
            reasonCategory);

    private static Task<Result> CancelWithAsync(
        IFamiliesDbContext dbContext,
        IBookingCancellationFactRecorder cancellationFactRecorder,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        UserId actorUserId,
        DateTime utcNow,
        string? reason = null,
        int? reasonCategory = null) =>
        new CancelBookingCommandHandler(dbContext, paymob, cancellationFactRecorder)
            .Handle(
                new CancelBookingCommand(bookingId, actorUserId, reason, reasonCategory, utcNow),
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
    /// The whole negative-first side effect contract: no fact row, the booking exactly as arranged, and
    /// the gateway never contacted.
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
    /// Fields every family cancellation fact carries, whatever the policy branch: family side, cancel
    /// action, the authorized actor, the caller's cancellation time, the current policy revision and no
    /// caregiver incident.
    /// </summary>
    private static void AssertFactSpine(
        BookingCancellationFact fact,
        BookingId bookingId,
        UserId actorUserId,
        DateTime cancelledOnUtc)
    {
        Assert.Equal(bookingId, fact.BookingId);
        Assert.Equal(actorUserId, fact.ActorUserId);
        Assert.Equal(BookingCancellationActorSide.Family, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, fact.Action);
        Assert.Equal(cancelledOnUtc, fact.CancelledOnUtc);
        Assert.Equal(BookingCancellationPolicy.CurrentPolicyVersion, fact.PolicyVersion);
        Assert.False(fact.IsCaregiverIncident);
    }

    private static DateTime ScheduledStart(Booking booking) =>
        booking.BookingDate.ToDateTime(booking.StartTime);

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

    /// <summary>
    /// The persistence seam the handler depends on, with a failing save. A subclass of
    /// <see cref="FamiliesDbContext"/> is not available — the production context is sealed — so the save is
    /// intercepted at the interface the handler actually consumes; reads are served by the real context,
    /// which is also the context the production recorder tracks the fact on.
    /// </summary>
    private sealed class SaveThrowingContext(FamiliesDbContext inner, Exception failure) : IFamiliesDbContext
    {
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

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int>(failure);
    }
}
