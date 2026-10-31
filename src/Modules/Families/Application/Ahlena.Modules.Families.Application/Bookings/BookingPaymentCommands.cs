using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Families.Application.Bookings;

/// <summary>
/// E04 platform-control gate for the named NEW operations only: a new caregiver
/// booking checkout, a new Care Home booking checkout, and new payment-intent
/// creation for caregiver bookings, Care Home bookings, subscription
/// initial/renewal payments and plan changes. Every check reads the persisted
/// control record through <see cref="IPlatformControlReader"/> and fails closed
/// with the generic unavailable error when it cannot be read: no request may
/// bypass the gate by assuming Operational.
/// Verified provider callbacks, existing intent readback/reuse, reads,
/// cancellations, refunds, care-delivery lifecycle and other unlisted
/// operations deliberately do not call this gate.
/// </summary>
public static class PlatformControlGate
{
    /// <summary>Maintenance gate for a new checkout that starts no provider call.</summary>
    public static async Task<Error?> CheckNewCheckoutAsync(
        IPlatformControlReader controls,
        CancellationToken cancellationToken)
    {
        var (snapshot, error) = await ReadAsync(controls, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        return snapshot!.Values.State == PlatformState.Maintenance
            ? PlatformControlErrors.Maintenance
            : null;
    }

    /// <summary>
    /// Maintenance and method gate for a caregiver/booking payment intent.
    /// ApplePay is deliberately unsupported by E04 and is rejected here, so it
    /// is never handed to the gateway.
    /// </summary>
    public static Task<Error?> CheckNewPaymentIntentAsync(
        IPlatformControlReader controls,
        IPaymentMethodConfiguration methods,
        PaymentMethod method,
        CancellationToken cancellationToken) =>
        CheckAsync(
            controls,
            methods,
            method switch
            {
                PaymentMethod.Card => MethodAvailability.Card,
                PaymentMethod.Wallet => MethodAvailability.Wallet,
                _ => MethodAvailability.Unsupported
            },
            cancellationToken);

    /// <summary>Maintenance and method gate for a subscription payment intent.</summary>
    public static Task<Error?> CheckNewPaymentIntentAsync(
        IPlatformControlReader controls,
        IPaymentMethodConfiguration methods,
        SubscriptionPaymentMethod method,
        CancellationToken cancellationToken) =>
        CheckAsync(
            controls,
            methods,
            // Exhaustive mapping: an undefined enum value (an unvalidated cast
            // reaching a direct handler call) must never be treated as a
            // supported method, so it fails closed as unavailable.
            method switch
            {
                SubscriptionPaymentMethod.Card => MethodAvailability.Card,
                SubscriptionPaymentMethod.Wallet => MethodAvailability.Wallet,
                _ => MethodAvailability.Unsupported
            },
            cancellationToken);

    private static async Task<Error?> CheckAsync(
        IPlatformControlReader controls,
        IPaymentMethodConfiguration methods,
        MethodAvailability method,
        CancellationToken cancellationToken)
    {
        var (snapshot, error) = await ReadAsync(controls, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        PlatformControlValues values = snapshot!.Values;

        if (values.State == PlatformState.Maintenance)
        {
            return PlatformControlErrors.Maintenance;
        }

        bool available = method switch
        {
            MethodAvailability.Card => PaymentAvailability.IsCardAvailable(values, methods),
            MethodAvailability.Wallet => PaymentAvailability.IsWalletAvailable(values, methods),
            _ => false
        };

        return available ? null : PlatformControlErrors.PaymentMethodUnavailable;
    }

    private static async Task<(PlatformControlSnapshot? Snapshot, Error? Error)> ReadAsync(
        IPlatformControlReader controls,
        CancellationToken cancellationToken)
    {
        try
        {
            PlatformControlSnapshot? snapshot = await controls.GetAsync(cancellationToken);

            return snapshot is null
                ? (null, PlatformControlErrors.Unavailable)
                : (snapshot, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, PlatformControlErrors.Unavailable);
        }
    }

    private enum MethodAvailability
    {
        Card = 1,
        Wallet = 2,
        Unsupported = 3
    }
}

public sealed record BookingPaymentIntentResponse(
    Guid BookingId,
    string PaymobOrderId,
    decimal Amount,
    string Currency,
    string ClientSecret,
    string PublicKey);

public sealed record CreateBookingPaymentIntentCommand(
    BookingId BookingId,
    UserId UserId,
    PaymentMethod Method,
    PaymobBillingData Billing,
    DateTime UtcNow) : ICommand<BookingPaymentIntentResponse>;

public sealed class CreateBookingPaymentIntentCommandValidator : AbstractValidator<CreateBookingPaymentIntentCommand>
{
    public CreateBookingPaymentIntentCommandValidator()
    {
        RuleFor(c => c.Method)
            .IsInEnum();

        RuleFor(c => c.Billing.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(c => c.Billing.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(c => c.Billing.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(c => c.Billing.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+?\d{8,15}$")
            .WithMessage("A valid phone number is required.");
    }
}

public sealed class CreateBookingPaymentIntentCommandHandler
    : ICommandHandler<CreateBookingPaymentIntentCommand, BookingPaymentIntentResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IPlatformControlReader _controls;
    private readonly IPaymentMethodConfiguration _paymentMethods;

    public CreateBookingPaymentIntentCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IPlatformControlReader controls,
        IPaymentMethodConfiguration paymentMethods)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _controls = controls;
        _paymentMethods = paymentMethods;
    }

    public async Task<Result<BookingPaymentIntentResponse>> Handle(
        CreateBookingPaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
        // E04: creating a new intent is gated before any provider call or
        // pending-transaction write, so a rejection leaves no partial payment
        // state. Existing intents and callbacks are handled elsewhere.
        Error? platformError = await PlatformControlGate.CheckNewPaymentIntentAsync(
            _controls,
            _paymentMethods,
            request.Method,
            cancellationToken);

        if (platformError is not null)
        {
            return Result<BookingPaymentIntentResponse>.Failure(platformError);
        }

        var family = await _dbContext.Families
            .AsNoTracking()
            .Include(f => f.Members)
            .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

        if (family is null)
        {
            return Result<BookingPaymentIntentResponse>.Failure(
                new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
        }

        Booking? booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.FamilyId == family.Id, cancellationToken);

        if (booking is null)
        {
            return Result<BookingPaymentIntentResponse>.Failure(
                new Error("Bookings.NotFound", "Booking was not found in this family."));
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return Result<BookingPaymentIntentResponse>.Failure(
                new Error("Bookings.Domain.InvalidOperation", "Only bookings awaiting payment can start a payment."));
        }

        Result<PaymobPaymentIntent> intent = await _paymobClient.CreatePaymentIntentAsync(
            new PaymobPaymentIntentInput(
                booking.Id,
                request.Method,
                booking.PriceSnapshot.TotalPayableAmount,
                booking.PriceSnapshot.Currency,
                request.Billing),
            cancellationToken);

        if (!intent.IsSuccess)
        {
            return Result<BookingPaymentIntentResponse>.Failure(intent.Error);
        }

        // Best-effort compensation: if recording fails after the gateway call,
        // the orphaned Paymob intention expires unused (expiration = 3600).
        // PaymobOrderId is the booking id string: the exact key the webhook
        // returns as merchant_order_id when confirming the payment.
        booking.RecordPaymentIntent(
            intent.Value.PaymobOrderId,
            request.Method,
            request.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BookingPaymentIntentResponse>.Success(
            new BookingPaymentIntentResponse(
                booking.Id.Value,
                intent.Value.PaymobOrderId,
                booking.PriceSnapshot.TotalPayableAmount,
                booking.PriceSnapshot.Currency,
                intent.Value.ClientSecret,
                intent.Value.PublicKey));
    }
}

// ----------------------------- Confirm Payment (Paymob webhook) -----------------------------
// Machine-origin command: no validator. HMAC verification happens in the webhook
// controller before this handler is reached; idempotency + amount check here.

public sealed record ConfirmBookingPaymentCommand(
    string PaymobOrderId,
    long PaymobTransactionId,
    long AmountCents,
    bool Success,
    bool Pending,
    DateTime UtcNow) : ICommand<ConfirmBookingPaymentResponse>;

public sealed record ConfirmBookingPaymentResponse(Guid BookingId, string Outcome);

public sealed class ConfirmBookingPaymentCommandHandler
    : ICommandHandler<ConfirmBookingPaymentCommand, ConfirmBookingPaymentResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;

    public ConfirmBookingPaymentCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
    }

    public async Task<Result<ConfirmBookingPaymentResponse>> Handle(
        ConfirmBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(
                b => b.PaymentTransactions.Any(t => t.PaymobOrderId == request.PaymobOrderId),
                cancellationToken);

        if (booking is null)
        {
            return Result<ConfirmBookingPaymentResponse>.Failure(
                new Error("Bookings.NotFound", "No booking matches this Paymob order."));
        }

        PaymentTransaction? transaction = booking.PaymentTransactions.FirstOrDefault(
            t => t.PaymobOrderId == request.PaymobOrderId
                && t.Status == PaymentTransactionStatus.Pending);

        if (transaction is null)
        {
            // Duplicate/out-of-order webhook (already settled) — idempotent no-op.
            return Result<ConfirmBookingPaymentResponse>.Success(
                new ConfirmBookingPaymentResponse(booking.Id.Value, "AlreadyProcessed"));
        }

        long expectedCents = (long)decimal.Round(transaction.Amount * 100m, 0, MidpointRounding.ToEven);

        if (expectedCents != request.AmountCents)
        {
            return Result<ConfirmBookingPaymentResponse>.Failure(
                new Error("Paymob.AmountMismatch",
                    "Webhook amount does not match the recorded payment intent."));
        }

        string paymobTransactionId = request.PaymobTransactionId.ToString();

        if (!request.Success)
        {
            if (request.Pending)
            {
                return Result<ConfirmBookingPaymentResponse>.Success(
                    new ConfirmBookingPaymentResponse(booking.Id.Value, "Pending"));
            }

            booking.RecordPaymentFailure(request.PaymobOrderId, paymobTransactionId, request.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<ConfirmBookingPaymentResponse>.Success(
                new ConfirmBookingPaymentResponse(booking.Id.Value, "Failed"));
        }

        booking.MarkAsPaid(request.PaymobOrderId, paymobTransactionId, request.UtcNow);

        string outcome;

        // The late-payment refund is claimed and committed before the provider call, so the final
        // save may lose the row to a competing writer. Only a settled movement is protected here;
        // when no refund was attempted the ordinary save stays authoritative.
        long? claimedRefundAwardMinorUnits = null;
        long settledRefundMinorUnits = 0;

        // Late payment: the acceptance window closed while the gateway was processing.
        if (booking.Status == BookingStatus.PendingCaregiverApproval
            && request.UtcNow > booking.AcceptanceDeadlineUtc)
        {
            booking.Expire(request.UtcNow);

            // The automatic late-payment refund moves real money, so it follows the same
            // claim-before-send rule as every policy-awarded settlement: the attempt, its exact
            // award and its correlation id are committed first, which makes it exclusive and
            // leaves durable evidence if the process dies before the outcome is saved.
            long refundableMinorUnits = booking.RefundableCeilingMinorUnits();

            if (refundableMinorUnits > 0)
            {
                Guid claimId = Guid.NewGuid();
                booking.ClaimRefundAttempt(claimId, refundableMinorUnits, request.UtcNow);
                claimedRefundAwardMinorUnits = refundableMinorUnits;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                paymobTransactionId,
                transaction.Amount,
                cancellationToken);

            if (refund.IsSuccess)
            {
                if (refundableMinorUnits > 0)
                {
                    booking.RecordRefundedAmount(refundableMinorUnits, request.UtcNow);
                    settledRefundMinorUnits = refundableMinorUnits;
                }

                booking.MarkRefunded(refund.Value, request.UtcNow);
                booking.ReleaseRefundAttempt(request.UtcNow);
                outcome = "PaidExpired";
            }
            else
            {
                // An unconfirmed outcome must not be retried blindly; a definitive refusal proves
                // nothing moved, so the claim is released and the settlement can be retried.
                if (refundableMinorUnits > 0
                    && string.Equals(
                        refund.Error?.Code,
                        "Paymob.RefundOutcomeUnknown",
                        StringComparison.Ordinal))
                {
                    booking.MarkRefundOutcomeAmbiguous(
                        paymobTransactionId,
                        refund.Error!.Code,
                        request.UtcNow,
                        refundableMinorUnits);
                }
                else
                {
                    booking.ReleaseRefundAttempt(request.UtcNow);
                }

                outcome = "PaidExpiredRefundPending";
            }
        }
        else
        {
            outcome = "Paid";
        }

        if (claimedRefundAwardMinorUnits is not null)
        {
            // A late provider confirmation is saved idempotently even if a competing writer won
            // the row; an unrecorded movement leaves the committed claim in flight so the attempt
            // stays blocked until an operator reconciles it (decision #17).
            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    settledRefundMinorUnits,
                    cancellationToken))
            {
                outcome = "PaidExpiredRefundPending";
            }
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return Result<ConfirmBookingPaymentResponse>.Success(
            new ConfirmBookingPaymentResponse(booking.Id.Value, outcome));
    }
}