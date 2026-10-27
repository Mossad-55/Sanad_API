using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// W1 admin booking outcomes: pins the money behavior of
/// <c>AdminRecordBookingOutcomeCommandHandler</c> and its retry companion
/// <c>AdminRefundBookingCommandHandler</c> exactly as they exist today — admin-only reasons
/// (a provider cancellation or no-show asserted by an administrator) award the full capture and
/// settle it through the same executor the caregiver path uses; the recipient-inaccessible
/// disposition records NoRefundDue and moves no money; every unconfirmable provider answer either
/// marks the booking ambiguous (decision #17) or releases the claim as a definitive refusal.
/// <para>
/// Fixtures are built through public domain entry points and persisted through the in-memory
/// provider; the Paymob client is a recording fake that answers with the production error codes.
/// </para>
/// <para>Executed in the combined focused W1–W4 suite and full Release suite.</para>
/// </summary>
public sealed class AdminBookingOutcomeCommandTests
{
    private static readonly DateTime CreatedOnUtc = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AcceptedAtUtc = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledStartUtc = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutcomeAtUtc = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NearStartOutcomeAtUtc = new(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc);

    private static readonly UserId AdminActorId = UserId.New();

    private const int EmergencyCategory = (int)BookingCancellationReasonCategory.Emergency;

    [Theory]
    [InlineData(1)] // ProviderCancelled
    [InlineData(2)] // ProviderNoShow
    public async Task AdminOutcome_ProviderCancelOrNoShow_AwardsTheFullCapture_AndSettlesIt(
        int outcomeReason)
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, outcomeReason, EmergencyCategory, "provider cancelled the visit");

        Assert.True(result.IsSuccess, result.Error?.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking refunded = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

        // The full capture (400.00 EGP = 40000 piastres) was decided, sent and settled.
        Assert.Equal(BookingStatus.Refunded, refunded.Status);
        Assert.Equal(40000, refunded.RefundedMinorUnits);

        RefundCall call = Assert.Single(paymob.RefundCalls);
        Assert.Equal(400.00m, call.Amount);
    }

    [Fact]
    public async Task AdminOutcome_FullAward_RecordsTheAwardOnTheDurableFact()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, "provider cancelled the visit");

        Assert.True(result.IsSuccess, result.Error?.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        BookingCancellationFact fact = after.BookingCancellationFacts
            .AsNoTracking()
            .Single(f => f.BookingId == booking.Id);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(40000, fact.AwardedMinorUnits);
        Assert.Equal(AdminActorId, fact.ActorUserId);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal("provider cancelled the visit", fact.ReasonNote);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
    }

    [Fact]
    public async Task AdminOutcome_RecipientInaccessible_RecordsNoRefundDue_AndMovesNoMoney()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 4, EmergencyCategory, "the recipient was not reachable");

        Assert.True(result.IsSuccess, result.Error?.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking denied = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

        // The disposition is recorded but the capture stays: no provider call, no settled money.
        Assert.Equal(BookingStatus.CancelledByCaregiver, denied.Status);
        Assert.Equal(0, denied.RefundedMinorUnits);
        Assert.Empty(paymob.RefundCalls);

        BookingCancellationFact fact = after.BookingCancellationFacts
            .AsNoTracking()
            .Single(f => f.BookingId == booking.Id);

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, fact.RefundEntitlement);
        Assert.Equal(0, fact.AwardedMinorUnits);
    }

    [Fact]
    public async Task AdminOutcome_ForcedLatenessReason_IgnoresTheLatenessFacts()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient();

        // Recorded one hour before start, where the ordinary family path would read a partial
        // clause-2 share — and with lateness facts (0 minutes, no consent) that name no
        // lateness outcome at all. The admin's explicit reason 3 forces the full clause-4 award.
        Result result = await RecordOutcomeAsync(
            databaseName,
            paymob,
            booking.Id,
            3,
            EmergencyCategory,
            "lateness beyond the threshold",
            utcNow: NearStartOutcomeAtUtc,
            providerLatenessMinutes: 0,
            customerConsentedToLateness: false);

        Assert.True(result.IsSuccess, result.Error?.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking refunded = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

        Assert.Equal(BookingStatus.Refunded, refunded.Status);
        Assert.Equal(40000, refunded.RefundedMinorUnits);
    }

    // -----------------------------------------------------------------------------------------
    // Validation: unconfirmable outcomes must fail before any state moves.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task AdminOutcome_UnknownReason_IsRejected()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 999, EmergencyCategory, "anything");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Outcome.InvalidReason", result.Error.Code);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_MissingCategory_IsRejectedWithoutStateChange()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, null, "provider cancelled the visit");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Cancel.ReasonCategoryRequired", result.Error.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking untouched = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, untouched.Status);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_UnknownCategory_IsRejectedWithoutStateChange()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, 999, "provider cancelled the visit");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Cancel.ReasonCategoryInvalid", result.Error.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking untouched = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, untouched.Status);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_BlankNote_IsRejectedWithoutStateChange()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, "   ");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Cancel.ReasonRequired", result.Error.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking untouched = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, untouched.Status);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_OversizedNote_IsRejected()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, new string('x', 501));

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Domain.InvalidOperation", result.Error.Code);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_NonConfirmedBooking_IsRejectedWithoutStateChange()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidPendingApprovalBooking(databaseName, family, elderly);
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, "provider cancelled the visit");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Domain.InvalidOperation", result.Error.Code);

        using FamiliesDbContext after = CreateContext(databaseName);
        Booking untouched = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, untouched.Status);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_UnknownBooking_IsNotFound()
    {
        var paymob = new RecordingPaymobClient();

        Result result = await RecordOutcomeAsync(
            NewDatabaseName(), paymob, BookingId.New(), 1, EmergencyCategory, "anything");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
        Assert.Empty(paymob.RefundCalls);
    }

    // -----------------------------------------------------------------------------------------
    // Unconfirmable provider answers: the ambiguous marker (decision #17) and the refusal.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task AdminOutcome_UnknownProviderAnswer_MarksTheBookingAmbiguous_AndBlocksTheRetry()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient
        {
            RefundSucceeds = false,
            RefundOutcomeUnknown = true,
        };

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, "provider cancelled the visit");

        // The award is durable on the ambiguity marker; the ledger is left untouched.
        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.RefundOutcomeUnknown", result.Error.Code);
        Assert.Single(paymob.RefundCalls);

        using (FamiliesDbContext after = CreateContext(databaseName))
        {
            Booking marked = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

            Assert.True(marked.IsRefundOutcomeAmbiguous);
            Assert.Equal(40000, marked.RefundAmbiguityAwardMinorUnits);
            Assert.Equal(0, marked.RefundedMinorUnits);
            Assert.Equal(BookingStatus.CancelledByCaregiver, marked.Status);
        }

        // Decision #17: the operator's retry is refused before any further movement, and the
        // gateway is not contacted a second time.
        using (FamiliesDbContext retryContext = CreateContext(databaseName))
        {
            var retryHandler = new AdminRefundBookingCommandHandler(retryContext, paymob);

            Result<BookingDetailResponse> retry = await retryHandler.Handle(
                new AdminRefundBookingCommand(booking.Id, OutcomeAtUtc.AddMinutes(5)),
                CancellationToken.None);

            Assert.False(retry.IsSuccess);
            Assert.Equal("Bookings.RefundOutcomeAmbiguous", retry.Error.Code);
        }

        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task AdminOutcome_RefusedRefund_ReleasesTheClaim_AndKeepsARetryPossible()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedPaidConfirmedBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient { RefundSucceeds = false };

        Result result = await RecordOutcomeAsync(
            databaseName, paymob, booking.Id, 1, EmergencyCategory, "provider cancelled the visit");

        // A definitive refusal (Paymob.RefundRejected) proves no money moved: the claim is
        // released, nothing is marked ambiguous, and the capture is untouched.
        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.RefundRejected", result.Error.Code);
        Assert.Single(paymob.RefundCalls);

        using (FamiliesDbContext after = CreateContext(databaseName))
        {
            Booking refused = after.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

            Assert.False(refused.IsRefundAttemptInFlight);
            Assert.False(refused.IsRefundOutcomeAmbiguous);
            Assert.Equal(0, refused.RefundedMinorUnits);
            Assert.Equal(BookingStatus.CancelledByCaregiver, refused.Status);
        }

        // A bounded retry with a healthy gateway then completes the settled refund.
        paymob.RefundSucceeds = true;

        using (FamiliesDbContext retryContext = CreateContext(databaseName))
        {
            var retryHandler = new AdminRefundBookingCommandHandler(retryContext, paymob);

            Result<BookingDetailResponse> retry = await retryHandler.Handle(
                new AdminRefundBookingCommand(booking.Id, OutcomeAtUtc.AddMinutes(5)),
                CancellationToken.None);

            Assert.True(retry.IsSuccess, retry.Error?.Code);
        }

        Assert.Equal(2, paymob.RefundCalls.Count);

        using FamiliesDbContext settled = CreateContext(databaseName);
        Booking completed = settled.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Refunded, completed.Status);
        Assert.Equal(40000, completed.RefundedMinorUnits);
    }

    // -----------------------------------------------------------------------------------------
    // Fixtures.
    // -----------------------------------------------------------------------------------------

    private static string NewDatabaseName() => $"sanad-admin-outcome-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static (Family Family, Elderly Elderly) SeedFamily(string databaseName)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var family = Family.Create(UserId.New(), "Admin Outcome Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(CreatedOnUtc.AddYears(-70)),
            DateOnly.FromDateTime(CreatedOnUtc));

        context.Families.Add(family);
        context.Elderlies.Add(elderly);
        context.SaveChanges();

        return (family, elderly);
    }

    /// <summary>
    /// Seeds one paid, accepted (Confirmed) booking of 400.00 — a 40000-piastre capture, the
    /// figure the full clause-4 award must settle.
    /// </summary>
    private static Booking SeedPaidConfirmedBooking(string databaseName, Family family, Elderly elderly) =>
        SeedPaidBooking(databaseName, family, elderly, accept: true);

    /// <summary>Seeds one paid booking still awaiting caregiver approval (not Confirmed).</summary>
    private static Booking SeedPaidPendingApprovalBooking(
        string databaseName, Family family, Elderly elderly) =>
        SeedPaidBooking(databaseName, family, elderly, accept: false);

    private static Booking SeedPaidBooking(
        string databaseName,
        Family family,
        Elderly elderly,
        bool accept)
    {
        Booking booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc).AddHours(2),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 0m, 0m, 1),
            acceptanceDeadlineUtc: ScheduledStartUtc.AddHours(-2),
            currentDate: DateOnly.FromDateTime(CreatedOnUtc),
            createdOnUtc: CreatedOnUtc,
            refundPolicyVersion: BookingRefundPolicyVersion.ForNewBooking(
                scheduledStartPolicyActivated: true));

        booking.RecordPaymentIntent("order", PaymentMethod.Card, CreatedOnUtc);
        booking.MarkAsPaid("order", "txn", CreatedOnUtc);

        if (accept)
        {
            booking.AcceptByCaregiver(AcceptedAtUtc);
        }

        using FamiliesDbContext context = CreateContext(databaseName);
        context.Bookings.Add(booking);
        context.SaveChanges();

        return booking;
    }

    private static async Task<Result> RecordOutcomeAsync(
        string databaseName,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        int outcomeReason,
        int? reasonCategory,
        string reason,
        DateTime? utcNow = null,
        int? providerLatenessMinutes = null,
        bool customerConsentedToLateness = false,
        bool forceMajeure = false)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var handler = new AdminRecordBookingOutcomeCommandHandler(
            context,
            paymob,
            new BookingCancellationFactRecorder(context));

        return await handler.Handle(
            new AdminRecordBookingOutcomeCommand(
                AdminActorId,
                bookingId,
                outcomeReason,
                reason,
                reasonCategory,
                providerLatenessMinutes,
                customerConsentedToLateness,
                forceMajeure,
                utcNow ?? OutcomeAtUtc),
            CancellationToken.None);
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    /// <summary>
    /// Records every refund the executor sends and answers with the production Paymob client's
    /// error codes: a definitive refusal is <c>Paymob.RefundRejected</c>; an unconfirmable call is
    /// <c>Paymob.RefundOutcomeUnknown</c>.
    /// </summary>
    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private readonly List<RefundCall> _refundCalls = [];

        public IReadOnlyList<RefundCall> RefundCalls => _refundCalls;

        public bool RefundSucceeds { get; set; } = true;

        /// <summary>Reports "could not confirm" instead of a clean refusal — the #17 case.</summary>
        public bool RefundOutcomeUnknown { get; set; }

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

            if (RefundSucceeds)
            {
                return Task.FromResult(Result<string?>.Success("test-refund-transaction"));
            }

            return Task.FromResult(Result<string?>.Failure(new Error(
                RefundOutcomeUnknown ? "Paymob.RefundOutcomeUnknown" : "Paymob.RefundRejected",
                "The gateway did not confirm the refund.")));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Refund verification is not used by this test.")));
    }
}
