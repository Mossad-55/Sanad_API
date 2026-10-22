using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

public static class RentalPaymentErrors
{
    public static readonly Error NotFound =
        new("Rentals.Payment.NotFound", "No rental booking matches this payment order.");

    public static readonly Error InvalidOperation =
        new("Rentals.Payment.InvalidOperation", "Only a booking awaiting payment can start a payment.");
}

/// <summary>
/// E04 maintenance/method gate for the rental payment intent, mirroring the
/// Families booking-payment gate: maintenance blocks any new checkout before
/// any provider call or pending-transaction write, an unconfigured method is a
/// client error, and an unreadable control record fails closed. Verified
/// provider callbacks and reads deliberately do not pass this gate.
/// </summary>
internal static class RentalPlatformControlGate
{
    public static async Task<Error?> CheckNewPaymentIntentAsync(
        IPlatformControlReader controls,
        IPaymentMethodConfiguration methods,
        RentalPaymentMethod method,
        CancellationToken cancellationToken)
    {
        PlatformControlSnapshot? snapshot;
        try
        {
            snapshot = await controls.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return PlatformControlErrors.Unavailable;
        }

        if (snapshot is null)
            return PlatformControlErrors.Unavailable;

        if (snapshot.Values.State == PlatformState.Maintenance)
            return PlatformControlErrors.Maintenance;

        // Exhaustive mapping: an undefined enum value (an unvalidated cast
        // reaching a direct handler call) must never be treated as supported,
        // so it fails closed as unavailable.
        bool available = method switch
        {
            RentalPaymentMethod.Card => snapshot.Values.CardEnabled && methods.CardConfigured,
            RentalPaymentMethod.Wallet => snapshot.Values.WalletEnabled && methods.WalletConfigured,
            _ => false
        };

        return available ? null : PlatformControlErrors.PaymentMethodUnavailable;
    }
}

public sealed record RentalPaymentIntentResponse(
    Guid RentalBookingId,
    string PaymobOrderId,
    decimal Amount,
    string Currency,
    string ClientSecret,
    string PublicKey);

public sealed record CreateRentalPaymentIntentCommand(
    Guid RentalBookingId,
    Guid RequestedByUserId,
    RentalPaymentMethod Method,
    PaymobBillingData Billing,
    DateTime UtcNow) : ICommand<RentalPaymentIntentResponse>;

public sealed class CreateRentalPaymentIntentCommandHandler(
    IRentalsDbContext dbContext,
    IFamiliesDbContext familiesDbContext,
    IPaymobClient paymobClient,
    IPlatformControlReader controls,
    IPaymentMethodConfiguration paymentMethods)
    : ICommandHandler<CreateRentalPaymentIntentCommand, RentalPaymentIntentResponse>
{
    public async Task<Result<RentalPaymentIntentResponse>> Handle(
        CreateRentalPaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
        // E04: creating a new intent is gated before any provider call or
        // pending-transaction write, so a rejection leaves no partial payment
        // state. Existing intents and callbacks are handled elsewhere.
        Error? platformError = await RentalPlatformControlGate.CheckNewPaymentIntentAsync(
            controls, paymentMethods, request.Method, cancellationToken);
        if (platformError is not null)
            return Result<RentalPaymentIntentResponse>.Failure(platformError);

        var family = await familiesDbContext.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
        if (family is null)
            return Result<RentalPaymentIntentResponse>.Failure(RentalBookingErrors.FamilyNotFound);

        RentalBooking? booking = await dbContext.RentalBookings
            .SingleOrDefaultAsync(candidate => candidate.Id == request.RentalBookingId && candidate.FamilyId == family.Id.Value, cancellationToken);
        if (booking is null)
            return Result<RentalPaymentIntentResponse>.Failure(RentalBookingErrors.NotFound);

        // A booking awaiting payment that already holds a pending intent gets a
        // fresh attempt: the previous intention expires unused at the gateway
        // (the Families booking-payment compensation semantics).
        if (booking.Status != RentalBookingStatus.PendingPayment)
            return Result<RentalPaymentIntentResponse>.Failure(RentalPaymentErrors.InvalidOperation);

        long totalMinorUnits = booking.PrepaidRentMinorUnits + booking.DepositMinorUnits;
        string merchantReference = $"rnt_{Guid.CreateVersion7():N}";

        Result<PaymobPaymentIntent> intent = await paymobClient.CreatePaymentIntentAsync(
            new PaymobPaymentIntentInput(
                new BookingId(booking.Id),
                MapMethod(request.Method),
                totalMinorUnits / 100m,
                "EGP",
                request.Billing,
                merchantReference),
            cancellationToken);

        if (!intent.IsSuccess)
            return Result<RentalPaymentIntentResponse>.Failure(intent.Error);

        // Best-effort compensation: if recording fails after the gateway call,
        // the orphaned Paymob intention expires unused (expiration = 3600).
        // The stored order id is the merchant reference: the exact key the
        // webhook returns as merchant_order_id when confirming the payment.
        booking.OpenPaymentTransaction(merchantReference, request.Method, request.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RentalPaymentIntentResponse>.Success(
            new RentalPaymentIntentResponse(
                booking.Id,
                merchantReference,
                totalMinorUnits / 100m,
                "EGP",
                intent.Value.ClientSecret,
                intent.Value.PublicKey));
    }

    private static Sanad.Modules.Families.Domain.Bookings.PaymentMethod MapMethod(RentalPaymentMethod method) => method switch
    {
        RentalPaymentMethod.Card => Sanad.Modules.Families.Domain.Bookings.PaymentMethod.Card,
        RentalPaymentMethod.Wallet => Sanad.Modules.Families.Domain.Bookings.PaymentMethod.Wallet,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}

// ----------------------- Confirm Payment (Paymob webhook) -----------------------
// Machine-origin command: no validator. HMAC verification happens in the
// webhook controller before this handler is reached; idempotency + amount
// check here, mirroring the Families confirm handler.

public sealed record ConfirmRentalPaymentCommand(
    string PaymobOrderId,
    long PaymobTransactionId,
    long AmountCents,
    bool Success,
    bool Pending,
    DateTime UtcNow) : ICommand<ConfirmRentalPaymentResponse>;

public sealed record ConfirmRentalPaymentResponse(Guid RentalBookingId, string Outcome);

public sealed class ConfirmRentalPaymentCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<ConfirmRentalPaymentCommand, ConfirmRentalPaymentResponse>
{
    public async Task<Result<ConfirmRentalPaymentResponse>> Handle(
        ConfirmRentalPaymentCommand request,
        CancellationToken cancellationToken)
    {
        RentalBooking? booking = await dbContext.RentalBookings
            .Include(candidate => candidate.PaymentTransactions)
            .SingleOrDefaultAsync(
                candidate => candidate.PaymentTransactions.Any(transaction => transaction.PaymobOrderId == request.PaymobOrderId),
                cancellationToken);

        if (booking is null)
            return Result<ConfirmRentalPaymentResponse>.Failure(RentalPaymentErrors.NotFound);

        RentalPaymentTransaction? transaction = booking.PaymentTransactions.FirstOrDefault(
            candidate => candidate.PaymobOrderId == request.PaymobOrderId
                && candidate.Status == RentalPaymentTransactionStatus.Pending);

        if (transaction is null)
        {
            // A duplicate/out-of-order webhook (already settled) is an
            // idempotent no-op — never a state rewrite.
            return Result<ConfirmRentalPaymentResponse>.Success(
                new ConfirmRentalPaymentResponse(booking.Id, "AlreadyProcessed"));
        }

        if (transaction.AmountMinorUnits != request.AmountCents)
        {
            return Result<ConfirmRentalPaymentResponse>.Failure(
                new Error("Paymob.AmountMismatch", "Webhook amount does not match the recorded payment intent."));
        }

        if (request.PaymobTransactionId <= 0)
            return Result<ConfirmRentalPaymentResponse>.Failure(new Error("Paymob.AmountMismatch", "Webhook carries no payment reference."));

        if (!request.Success)
        {
            if (request.Pending)
            {
                return Result<ConfirmRentalPaymentResponse>.Success(
                    new ConfirmRentalPaymentResponse(booking.Id, "Pending"));
            }

            transaction.MarkFailed(request.PaymobTransactionId.ToString(), request.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ConfirmRentalPaymentResponse>.Success(
                new ConfirmRentalPaymentResponse(booking.Id, "Failed"));
        }

        booking.ConfirmPayment(transaction, request.PaymobTransactionId, request.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ConfirmRentalPaymentResponse>.Success(
            new ConfirmRentalPaymentResponse(booking.Id, "Paid"));
    }
}
