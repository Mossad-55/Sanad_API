using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Payments;
using Xunit;

namespace Sanad.UnitTests.API;

/// <summary>
/// Regression coverage for the care-home refund webhook. Paymob reports a
/// refund as a *child* transaction (<c>is_refund=true</c>,
/// <c>has_parent_transaction=true</c>, <c>is_refunded=false</c>) whose *parent*
/// payment carries <c>is_refunded=true</c>; the pre-fix handler dropped that
/// child callback because it keyed completion on the child's own
/// <c>is_refunded</c> flag, leaving a provider-settled refund stuck at
/// <c>Initiated</c>. Completion is now matched on HMAC-covered identity, and only
/// on the transaction id (the stored refund reference, or the refunded payment),
/// so only a settled refund may advance a booking. The signed <c>order.id</c> is
/// used only to recognise a raced callback — never to complete.
/// <para>
/// The refund HTTP response persists <c>RefundReference</c> only after the
/// provider call returns, so a settled callback can race that write (the attempt
/// is still claimed). Such a callback matches only by the signed order id, which
/// does <b>not</b> prove the transaction is a refund (<c>is_refund</c> is not
/// signed), so it is refused as not-yet-applicable rather than used to complete —
/// a non-refund child sharing the order and amount must never fabricate a
/// completion. The refusal keeps the settlement from being silently dropped while
/// the processor persists the reference, after which the callback completes
/// idempotently.
/// </para>
/// </summary>
public sealed class CareHomeRefundWebhookCallbackTests
{
    private const string Secret = "test-refund-secret";
    private const long CaptureTransactionId = 561442756;
    private const long RefundTransactionId = 561442898;
    private const long MerchantOrderId = 640412142;
    private const long AmountCents = 1_150_000;
    private const string MerchantReference = "chb_01a17ac86e2475c8b5962d70499fd2a4";

    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    // ---- controller: signed raw callbacks (real HMAC, real composition) ----

    [Fact]
    public async Task Signed_child_refund_callback_dispatches_a_settled_refund_command()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(null, Signed(Secret, ChildRefundObject()), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<CompleteCareHomeRefundCallbackCommand>(sender.LastRequest);
        Assert.Equal(RefundTransactionId, command.TransactionId);
        Assert.True(command.Success);
        Assert.False(command.Pending);
        // The child refund transaction itself is NOT marked refunded.
        Assert.False(command.IsRefunded);
        Assert.Equal(AmountCents, command.AmountCents);
        // The HMAC-covered order id is carried for the not-yet-referenced race.
        Assert.Equal(MerchantOrderId, command.OrderId);
    }

    [Fact]
    public async Task Signed_refunded_parent_callback_dispatches_a_settled_refund_command()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(null, Signed(Secret, ParentRefundObject()), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<CompleteCareHomeRefundCallbackCommand>(sender.LastRequest);
        Assert.Equal(CaptureTransactionId, command.TransactionId);
        Assert.True(command.Success);
        Assert.False(command.Pending);
        Assert.True(command.IsRefunded);
    }

    [Fact]
    public async Task Signed_void_child_callback_is_acknowledged_without_any_dispatch()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(null, Signed(Secret, VoidObject()), default);

        Assert.IsType<OkResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Signed_voided_top_level_payment_still_reaches_the_payment_handler()
    {
        // The void guard is scoped to the refund/child branch: an ordinary
        // (non-child) payment payload whose is_voided flag is set keeps its
        // existing payment routing and is never silently swallowed.
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, VoidedPaymentObject(MerchantReference)), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<ConfirmCareHomePaymentCommand>(sender.LastRequest);
        Assert.Equal(MerchantReference, command.MerchantReference);
    }

    [Fact]
    public async Task Child_refund_callback_signed_with_the_wrong_secret_is_rejected()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(null, Signed("wrong-secret", ChildRefundObject()), default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Child_refund_callback_without_any_hmac_is_rejected()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        using JsonDocument unsigned = JsonDocument.Parse($"{{\"obj\":{ChildRefundObject()}}}");

        IActionResult result = await controller.HandlePaymob(null, unsigned.RootElement.Clone(), default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Child_refund_callback_missing_the_pending_flag_is_rejected_as_malformed()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, ChildRefundObjectWithoutPending()), default);

        Assert.IsType<BadRequestResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Child_refund_callback_missing_the_transaction_id_is_rejected_as_malformed()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, ChildRefundObjectWithoutId()), default);

        Assert.IsType<BadRequestResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task A_handler_failure_is_never_acknowledged_as_applied()
    {
        // A completion that could not be persisted must be a retriable non-2xx,
        // never a silent 200 that would let the provider forget the outcome.
        var sender = new RecordingSender(
            Result.Failure(new Error("CareHomes.Refund.CallbackConcurrency", "raced a concurrent write")));
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(null, Signed(Secret, ChildRefundObject()), default);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public async Task Signed_payment_callback_still_reaches_the_care_home_payment_handler()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, PaymentObject(MerchantReference)), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<ConfirmCareHomePaymentCommand>(sender.LastRequest);
        Assert.Equal(MerchantReference, command.MerchantReference);
    }

    [Fact]
    public async Task Signed_subscription_callback_still_reaches_the_subscription_handler()
    {
        var sender = new RecordingSender(
            Result<ConfirmSubscriptionPaymentResponse>.Success(new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid")));
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, PaymentObject("sub_0198e2c2-7777-7777-8888-0000000000bb")), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<ConfirmSubscriptionPaymentCommand>(sender.LastRequest);
        Assert.Equal("sub_0198e2c2-7777-7777-8888-0000000000bb", command.MerchantReference);
    }

    [Fact]
    public async Task Signed_booking_callback_still_reaches_the_booking_handler()
    {
        var sender = new RecordingSender(
            Result<ConfirmBookingPaymentResponse>.Success(new ConfirmBookingPaymentResponse(Guid.NewGuid(), "Paid")));
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null, Signed(Secret, PaymentObject("bk_0198e2c2-7777-7777-8888-0000000000ff")), default);

        Assert.IsType<OkResult>(result);
        var command = Assert.IsType<ConfirmBookingPaymentCommand>(sender.LastRequest);
        Assert.Equal("bk_0198e2c2-7777-7777-8888-0000000000ff", command.PaymobOrderId);
    }

    // ---- handler + domain: real state transitions ----

    [Fact]
    public async Task Child_refund_callback_completes_the_booking()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeBookingStatus.Refunded, booking.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Refunded, booking.PaymentStatus);
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
        Assert.NotNull(booking.RefundCompletedOnUtc);
        Assert.Single(db.BookingNotificationOutbox,
            x => x.BookingId == booking.Id && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [Fact]
    public async Task Refunded_parent_callback_completes_the_booking()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                CaptureTransactionId, true, false, true, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.Refunded, booking.Status);
    }

    [Fact]
    public async Task Pending_child_refund_callback_does_not_complete()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, true, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, booking.Status);
        Assert.Null(booking.RefundCompletedOnUtc);
        Assert.Empty(db.BookingNotificationOutbox);
    }

    [Fact]
    public async Task Unsuccessful_child_refund_callback_does_not_complete()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, false, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);
        Assert.Null(booking.RefundCompletedOnUtc);
    }

    [Fact]
    public async Task Unrelated_child_transaction_does_not_complete()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        // A void or other child transaction that is neither our stored reference,
        // our payment id nor our intention order must not touch the booking.
        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                999_999, true, false, false, AmountCents, 111_111, Now.AddMinutes(5)), default);

        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);
        Assert.Null(booking.RefundCompletedOnUtc);
        Assert.Empty(db.BookingNotificationOutbox);
    }

    [Fact]
    public async Task Payment_id_without_a_refunded_marker_does_not_complete()
    {
        // A capture/child transaction that happens to carry the payment's id but
        // is not marked refunded must never complete a refund.
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                CaptureTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);
        Assert.Null(booking.RefundCompletedOnUtc);
    }

    [Fact]
    public async Task Child_refund_callback_with_a_wrong_amount_does_not_complete()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();

        await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents - 1, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);
        Assert.Null(booking.RefundCompletedOnUtc);
    }

    [Fact]
    public async Task Replayed_child_refund_callback_is_idempotent()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await InitiatedRefundAsync();
        var handler = new CompleteCareHomeRefundCallbackHandler(db);
        var command = new CompleteCareHomeRefundCallbackCommand(
            RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5));

        await handler.Handle(command, default);
        await handler.Handle(command, default);

        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
        Assert.Single(db.BookingNotificationOutbox,
            x => x.BookingId == booking.Id && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    // ---- the refund-callback / refund-HTTP race ----

    [Fact]
    public async Task Raced_child_refund_callback_is_refused_transiently_and_never_completes()
    {
        // The provider settled the refund while the attempt was still claimed and
        // its reference had not been persisted yet. The callback matches only by
        // the HMAC-covered order id, which does not prove the transaction is a
        // refund, so the handler must refuse it as not-yet-applicable — never
        // complete the booking — while the processor finishes and persists the
        // reference. The refusal (not a silent 200) is what keeps the settlement
        // from being dropped.
        (CareHomesDbContext db, CareHomeBooking booking) = await ClaimedRefundAwaitingReferenceAsync();
        Assert.Null(booking.RefundReference);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.NotNull(booking.RefundClaimedOnUtc);

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.Refund.CallbackNotYetApplicable", result.Error.Code);
        Assert.Null(booking.RefundReference);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundPending, booking.Status);
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.Null(booking.RefundCompletedOnUtc);
        Assert.Empty(db.BookingNotificationOutbox);
    }

    [Fact]
    public async Task Refused_raced_callback_completes_once_the_reference_is_persisted()
    {
        // The same callback, ordered as production does it: the raced delivery is
        // refused while the refund call is in flight; the processor then persists
        // the reference; the redelivered callback now matches the stored reference
        // and completes the refund exactly once.
        (CareHomesDbContext db, CareHomeBooking booking) = await ClaimedRefundAwaitingReferenceAsync();
        var handler = new CompleteCareHomeRefundCallbackHandler(db);
        var command = new CompleteCareHomeRefundCallbackCommand(
            RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5));

        Result raced = await handler.Handle(command, default);
        Assert.True(raced.IsFailure);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);

        // The refund processor's provider call returns and persists the reference.
        booking.MarkRefundInitiated(RefundTransactionId.ToString(), Now.AddMinutes(6));
        await db.SaveChangesAsync();
        Assert.Equal(CareHomeRefundStatus.Initiated, booking.RefundStatus);

        // The provider redelivers the callback: it now matches the stored reference.
        Result redelivered = await handler.Handle(command, default);

        Assert.True(redelivered.IsSuccess);
        Assert.Equal(RefundTransactionId.ToString(), booking.RefundReference);
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.Refunded, booking.Status);
        Assert.Single(db.BookingNotificationOutbox,
            x => x.BookingId == booking.Id && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [Fact]
    public async Task Non_refund_child_sharing_order_and_amount_never_completes_a_claimed_refund()
    {
        // The exact false-completion risk: a settled, successful transaction that is
        // NOT a refund — is_refund is not signed, so has_parent_transaction alone
        // proves nothing — but which shares the booking's signed order id and its
        // recorded award, arriving while the refund is claimed in flight. It must be
        // refused, never completed.
        (CareHomesDbContext db, CareHomeBooking booking) = await ClaimedRefundAwaitingReferenceAsync();

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                999_999, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsFailure);
        Assert.Null(booking.RefundReference);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundPending, booking.Status);
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.Null(booking.RefundCompletedOnUtc);
        Assert.Empty(db.BookingNotificationOutbox);
    }

    [Fact]
    public async Task Raced_callback_with_a_wrong_amount_does_not_complete()
    {
        (CareHomesDbContext db, CareHomeBooking booking) = await ClaimedRefundAwaitingReferenceAsync();

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents - 1, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Null(booking.RefundReference);
        Assert.Null(booking.RefundCompletedOnUtc);
    }

    [Fact]
    public async Task Raced_callback_for_an_unclaimed_refund_does_not_complete()
    {
        // The order id matches and the award matches, but no provider call is in
        // flight (the attempt was never claimed): nothing is written and no refund
        // is fabricated.
        await using CareHomesDbContext db = NewDb();
        CareHomeBooking booking = CreateBooking();
        booking.SetPaymentIntent(1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Null(booking.RefundClaimedOnUtc);

        Result result = await new CompleteCareHomeRefundCallbackHandler(db)
            .Handle(new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Null(booking.RefundReference);
        Assert.Null(booking.RefundCompletedOnUtc);
        Assert.Empty(db.BookingNotificationOutbox);
    }

    // ---- the authenticated readback: completion without any callback ----

    [Fact]
    public async Task Raced_callback_without_redelivery_is_completed_by_the_verified_readback()
    {
        // The ordering production actually has when the provider never
        // redelivers: the attempt is claimed, the refund HTTP call is in flight,
        // the provider delivers the settled callback before the reference is
        // persisted (the handler refuses it), the refund call then returns its
        // id, and the authenticated readback verifies and completes the refund —
        // with NO callback redelivery at all.
        const string databaseName = "round3-raced-callback-readback";
        Guid bookingId;
        await using (var seed = new CareHomesDbContext(DbOptions(databaseName)))
        {
            CareHomeBooking seeded = PaidCancelledBooking();
            seed.Bookings.Add(seeded);
            await seed.SaveChangesAsync();
            bookingId = seeded.Id;
        }

        await using var db = new CareHomesDbContext(DbOptions(databaseName));
        CareHomeBooking booking = await db.Bookings.SingleAsync(x => x.Id == bookingId);

        Result? raced = null;
        var paymob = new VerifyingPaymobClient(SettledRefundView())
        {
            OnRefundCall = async () =>
            {
                // A separate context, exactly as the webhook request has: it
                // observes the committed claim and no reference yet.
                await using var callbackDb = new CareHomesDbContext(DbOptions(databaseName));
                raced = await new CompleteCareHomeRefundCallbackHandler(callbackDb).Handle(
                    new CompleteCareHomeRefundCallbackCommand(
                        RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)),
                    default);
            }
        };

        Result initiated = await CareHomeRefundProcessor.InitiateAsync(
            db, paymob, booking, Now.AddMinutes(3), default);

        Assert.True(initiated.IsSuccess);
        Assert.NotNull(raced);
        Assert.True(raced!.IsFailure);
        Assert.Equal("CareHomes.Refund.CallbackNotYetApplicable", raced.Error.Code);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(1, paymob.VerifyCalls);
        Assert.Equal(RefundTransactionId.ToString(), paymob.LastVerifiedId);

        await using var verify = new CareHomesDbContext(DbOptions(databaseName));
        CareHomeBooking persisted = await verify.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(CareHomeRefundStatus.Completed, persisted.RefundStatus);
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Equal(CareHomeBookingStatus.Refunded, persisted.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Refunded, persisted.PaymentStatus);
        Assert.Single(verify.BookingNotificationOutbox,
            x => x.BookingId == bookingId && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [Fact]
    public async Task Verified_readback_completes_and_a_second_attempt_adds_no_event()
    {
        const string databaseName = "round3-idempotent-readback";
        Guid bookingId;
        await using (var seed = new CareHomesDbContext(DbOptions(databaseName)))
        {
            CareHomeBooking seeded = PaidCancelledBooking();
            seed.Bookings.Add(seeded);
            await seed.SaveChangesAsync();
            bookingId = seeded.Id;
        }

        await using var db = new CareHomesDbContext(DbOptions(databaseName));
        CareHomeBooking booking = await db.Bookings.SingleAsync(x => x.Id == bookingId);
        var paymob = new VerifyingPaymobClient(SettledRefundView());

        Result first = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, Now.AddMinutes(3), default);
        Result second = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, Now.AddMinutes(4), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);

        await using var verify = new CareHomesDbContext(DbOptions(databaseName));
        Assert.Single(verify.BookingNotificationOutbox,
            x => x.BookingId == bookingId && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [Fact]
    public async Task Pending_readback_leaves_the_refund_initiated_and_never_repeats_the_refund_post()
    {
        (CareHomeBooking persisted, VerifyingPaymobClient paymob, int completed) =
            await RunReadbackAsync(SettledRefundView() with { Pending = true });

        Assert.Equal(CareHomeRefundStatus.Initiated, persisted.RefundStatus);
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Null(persisted.RefundCompletedOnUtc);
        Assert.Equal(0, completed);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(1, paymob.VerifyCalls);
    }

    [Fact]
    public async Task Unavailable_readback_leaves_the_refund_initiated_with_a_persisted_reference()
    {
        // The gateway client could not read the transaction back at all: never a
        // fabricated settled refund, never a repeated refund POST, and the
        // reference stays for the callback/operator fallback.
        (CareHomeBooking persisted, VerifyingPaymobClient paymob, int completed) =
            await RunReadbackAsync(view: null, verificationError: new Error("Paymob.VerificationUnavailable", "down"));

        Assert.Equal(CareHomeRefundStatus.Initiated, persisted.RefundStatus);
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Null(persisted.RefundCompletedOnUtc);
        Assert.Equal(0, completed);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(1, paymob.VerifyCalls);
    }

    [Fact]
    public async Task A_null_refund_reference_skips_the_readback_entirely()
    {
        // A 2xx that carried no usable reference cannot be read back; the
        // readback is not attempted and nothing is completed.
        (CareHomeBooking persisted, VerifyingPaymobClient paymob, int completed) =
            await RunReadbackAsync(view: null, nullRefund: true);

        Assert.Equal(0, paymob.VerifyCalls);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Null(persisted.RefundReference);
        Assert.Equal(CareHomeRefundStatus.Initiated, persisted.RefundStatus);
        Assert.Equal(0, completed);
    }

    [Theory]
    [InlineData("is_refund")]
    [InlineData("success")]
    [InlineData("pending")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("order")]
    [InlineData("parent")]
    [InlineData("transaction_id")]
    public async Task A_readback_that_does_not_exactly_match_the_booking_never_completes(string mismatch)
    {
        // Every identity/outcome cross-check is mandatory: a readback that is
        // not the exact settled refund of THIS booking (wrong kind, outcome,
        // amount, currency, order or original capture) must never complete it.
        PaymobRefundVerification view = mismatch switch
        {
            "is_refund" => SettledRefundView() with { IsRefund = false },
            "success" => SettledRefundView() with { Success = false },
            "pending" => SettledRefundView() with { Pending = true },
            "amount" => SettledRefundView() with { AmountCents = 1_149_999 },
            "currency" => SettledRefundView() with { Currency = "USD" },
            "order" => SettledRefundView() with { OrderId = 111_111 },
            "parent" => SettledRefundView() with { ParentTransactionId = 999_999 },
            _ => SettledRefundView() with { TransactionId = 999_999 }
        };

        (CareHomeBooking persisted, VerifyingPaymobClient paymob, int completed) = await RunReadbackAsync(view);

        Assert.Equal(CareHomeRefundStatus.Initiated, persisted.RefundStatus);
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Null(persisted.RefundCompletedOnUtc);
        Assert.Equal(0, completed);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(1, paymob.VerifyCalls);
    }

    [Fact]
    public void Refund_lifecycle_always_records_a_positive_award()
    {
        // The handler's amount guard is mandatory (never skipped when the award
        // is missing): the domain must therefore guarantee a positive award for
        // every booking inside the refund lifecycle.
        CareHomeBooking unstarted = CreateBooking();
        Assert.Null(unstarted.RefundAmount);
        Assert.False(unstarted.TryClaimRefund(Now.AddMinutes(1)));

        CareHomeBooking booking = CreateBooking();
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        Assert.NotNull(booking.RefundAmount);
        Assert.True(booking.RefundAmount > 0m);

        Assert.True(booking.TryClaimRefund(Now.AddMinutes(3)));
        Assert.True(booking.RefundAmount > 0m);
        booking.MarkRefundInitiated(RefundTransactionId.ToString(), Now.AddMinutes(4));
        Assert.True(booking.RefundAmount > 0m);
        Assert.True(booking.MarkRefundCompletedFromProviderCallback(
            RefundTransactionId.ToString(), RefundTransactionId, Now.AddMinutes(5)));
    }

    [Fact]
    public void Provider_callback_matcher_anchors_on_the_transaction_id()
    {
        CareHomeBooking booking = CreateBooking();
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(3)));
        booking.MarkRefundInitiated(RefundTransactionId.ToString(), Now.AddMinutes(4));

        Assert.False(booking.MarkRefundCompletedFromProviderCallback("999999", 999_999, Now.AddMinutes(5)));
        Assert.True(booking.MarkRefundCompletedFromProviderCallback(RefundTransactionId.ToString(), RefundTransactionId, Now.AddMinutes(5)));
        Assert.False(booking.MarkRefundCompletedFromProviderCallback(RefundTransactionId.ToString(), RefundTransactionId, Now.AddMinutes(6)));
        Assert.Equal(CareHomeRefundStatus.Completed, booking.RefundStatus);
    }

    // ---- helpers ----

    private static CareHomesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CareHomeBooking CreateBooking() =>
        CareHomeBooking.Create(
            CareHomeId.New(), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 5), 10000m, 1000m, 500m, 4, "ar", "Elderly", 75, null,
            "Contact", "+201000000000", "Daughter", null, Now);

    private static async Task<(CareHomesDbContext Db, CareHomeBooking Booking)> InitiatedRefundAsync()
    {
        CareHomesDbContext db = NewDb();
        CareHomeBooking booking = CreateBooking();
        booking.SetPaymentIntent(1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(3)));
        booking.MarkRefundInitiated(RefundTransactionId.ToString(), Now.AddMinutes(4));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (db, booking);
    }

    private static async Task<(CareHomesDbContext Db, CareHomeBooking Booking)> ClaimedRefundAwaitingReferenceAsync()
    {
        CareHomesDbContext db = NewDb();
        CareHomeBooking booking = CreateBooking();
        booking.SetPaymentIntent(1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        // Claimed, provider call in flight, reference not persisted yet.
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(3)));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (db, booking);
    }

    /// <summary>A paid, family-cancelled booking: refund pending, not yet claimed.</summary>
    private static CareHomeBooking PaidCancelledBooking()
    {
        CareHomeBooking booking = CreateBooking();
        booking.SetPaymentIntent(1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        return booking;
    }

    /// <summary>The provider's authenticated view of the settled refund child.</summary>
    private static PaymobRefundVerification SettledRefundView() =>
        new(RefundTransactionId, true, true, false, AmountCents, "EGP", MerchantOrderId, CaptureTransactionId);

    private static DbContextOptions<CareHomesDbContext> DbOptions(string databaseName) =>
        new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;

    /// <summary>
    /// Runs the refund processor against a freshly seeded paid+cancelled booking
    /// with a verifying gateway double, then reads the committed state back from
    /// a separate context (real persistence, not the tracked instance).
    /// </summary>
    private static async Task<(CareHomeBooking Persisted, VerifyingPaymobClient Paymob, int CompletedEvents)> RunReadbackAsync(
        PaymobRefundVerification? view,
        Error? verificationError = null,
        bool nullRefund = false)
    {
        string databaseName = "round3-readback-" + Guid.NewGuid().ToString("N");
        Guid bookingId;
        await using (var seed = new CareHomesDbContext(DbOptions(databaseName)))
        {
            CareHomeBooking seeded = PaidCancelledBooking();
            seed.Bookings.Add(seeded);
            await seed.SaveChangesAsync();
            bookingId = seeded.Id;
        }

        var paymob = new VerifyingPaymobClient(view, verificationError, nullRefund: nullRefund);
        await using (var db = new CareHomesDbContext(DbOptions(databaseName)))
        {
            CareHomeBooking booking = await db.Bookings.SingleAsync(x => x.Id == bookingId);
            Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, Now.AddMinutes(3), default);
            Assert.True(initiated.IsSuccess);
        }

        await using var verify = new CareHomesDbContext(DbOptions(databaseName));
        CareHomeBooking persisted = await verify.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        int completed = verify.BookingNotificationOutbox.Count(
            x => x.BookingId == bookingId && x.EventType == CareHomeNotificationEvent.RefundCompleted);
        return (persisted, paymob, completed);
    }

    private static PaymobWebhookController CreateController(ISender sender, string secret) =>
        new(sender, Options.Create(new PaymobOptions { HmacSecret = secret }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    /// <summary>
    /// A Paymob transaction payload signed with the real documented HMAC field
    /// order, exactly as the provider signs it.
    /// </summary>
    private static JsonElement Signed(string secret, string objJson)
    {
        using JsonDocument unsigned = JsonDocument.Parse(objJson);
        string hmac = PaymobHmacCalculator.Calculate(unsigned.RootElement, secret);
        using JsonDocument signed = JsonDocument.Parse($"{{\"obj\":{objJson},\"hmac\":\"{hmac}\"}}");
        return signed.RootElement.Clone();
    }

    /// <summary>
    /// The provider's refund child transaction, as read back from
    /// <c>GET /api/acceptance/transactions/561442898</c>: is_refund=true,
    /// is_refunded=false, has_parent_transaction=true, success=true.
    /// </summary>
    private static string ChildRefundObject() =>
        ChildRefundBody($"\"id\":{RefundTransactionId},", "\"pending\":false,");

    private static string ChildRefundObjectWithoutPending() =>
        ChildRefundBody($"\"id\":{RefundTransactionId},", string.Empty);

    private static string ChildRefundObjectWithoutId() =>
        ChildRefundBody(string.Empty, "\"pending\":false,");

    private static string ChildRefundBody(string idField, string pendingField) =>
        "{"
        + idField
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,"
        + pendingField
        + "\"currency\":\"EGP\",\"is_refund\":true,\"is_refunded\":false,"
        + "\"is_void\":false,\"is_voided\":false,\"has_parent_transaction\":true,"
        + $"\"parent_transaction\":{CaptureTransactionId},"
        + $"\"order\":{{\"id\":{MerchantOrderId},\"merchant_order_id\":\"{MerchantReference}\"}}}}";

    /// <summary>The refunded parent payment: is_refunded=true, refunded_amount_cents=full.</summary>
    private static string ParentRefundObject() =>
        "{"
        + $"\"id\":{CaptureTransactionId},"
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,\"pending\":false,\"currency\":\"EGP\",\"is_refund\":false,\"is_refunded\":true,"
        + "\"is_void\":false,\"is_voided\":false,\"has_parent_transaction\":false,\"parent_transaction\":null,"
        + $"\"order\":{{\"id\":{MerchantOrderId},\"merchant_order_id\":\"{MerchantReference}\"}}}}";

    private static string VoidObject() =>
        "{"
        + "\"id\":561442899,"
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,\"pending\":false,\"currency\":\"EGP\",\"is_refund\":false,\"is_refunded\":false,"
        + "\"is_void\":true,\"is_voided\":true,\"has_parent_transaction\":true,"
        + $"\"parent_transaction\":{CaptureTransactionId},"
        + $"\"order\":{{\"id\":{MerchantOrderId},\"merchant_order_id\":\"{MerchantReference}\"}}}}";

    private static string VoidedPaymentObject(string merchantOrderId) =>
        "{"
        + $"\"id\":{CaptureTransactionId},"
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,\"pending\":false,\"currency\":\"EGP\",\"is_refund\":false,\"is_refunded\":false,"
        + "\"is_void\":false,\"is_voided\":true,\"has_parent_transaction\":false,"
        + $"\"order\":{{\"id\":{MerchantOrderId},\"merchant_order_id\":\"{merchantOrderId}\"}}}}";

    private static string PaymentObject(string merchantOrderId) =>
        "{"
        + $"\"id\":{CaptureTransactionId},"
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,\"pending\":false,\"currency\":\"EGP\",\"is_refunded\":false,"
        + "\"is_voided\":false,\"has_parent_transaction\":false,"
        + $"\"order\":{{\"id\":{MerchantOrderId},\"merchant_order_id\":\"{merchantOrderId}\"}}}}";

    /// <summary>
    /// A gateway double whose refund call returns the exact provider refund id
    /// and whose readback returns a caller-supplied (or failed) authenticated
    /// view, so the processor's readback path is exercised without any provider
    /// call. Records how many times each path was taken.
    /// </summary>
    private sealed class VerifyingPaymobClient : IPaymobClient
    {
        private readonly PaymobRefundVerification? _verification;
        private readonly Error? _verificationError;
        private readonly string _refundId;
        private readonly bool _nullRefund;

        public VerifyingPaymobClient(
            PaymobRefundVerification? verification,
            Error? verificationError = null,
            string? refundId = null,
            bool nullRefund = false)
        {
            _verification = verification;
            _verificationError = verificationError;
            _refundId = refundId ?? RefundTransactionId.ToString();
            _nullRefund = nullRefund;
        }

        public Func<Task>? OnRefundCall { get; init; }

        public int RefundCalls { get; private set; }

        public int VerifyCalls { get; private set; }

        public string? LastVerifiedId { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.NotUsed", "Payment intent is not used by this test.")));

        public async Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            if (OnRefundCall is not null)
            {
                await OnRefundCall();
            }

            return Result<string?>.Success(_nullRefund ? null : _refundId);
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            LastVerifiedId = refundTransactionId;
            return Task.FromResult(_verificationError is not null
                ? Result<PaymobRefundVerification>.Failure(_verificationError)
                : Result<PaymobRefundVerification>.Success(_verification!));
        }
    }

    private sealed class RecordingSender(object response) : ISender
    {
        public List<object> Requests { get; } = [];

        public object? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
