using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record BookingPaymentIntentResponse(
    Guid BookingId,
    string PaymobOrderId,
    decimal Amount,
    string Currency,
    string? IframeUrl,
    string? WalletRedirectUrl);

public sealed record CreateBookingPaymentIntentCommand(
    BookingId BookingId,
    UserId UserId,
    PaymentMethod Method,
    string? WalletNumber,
    PaymobBillingData Billing,
    DateTime UtcNow) : ICommand<BookingPaymentIntentResponse>;

public sealed class CreateBookingPaymentIntentCommandValidator : AbstractValidator<CreateBookingPaymentIntentCommand>
{
    public CreateBookingPaymentIntentCommandValidator()
    {
        RuleFor(c => c.Method)
            .IsInEnum();

        RuleFor(c => c.WalletNumber)
            .NotEmpty()
            .Matches(@"^\d{10,15}$")
            .When(c => c.Method == PaymentMethod.Wallet)
            .WithMessage("A wallet phone number of 10-15 digits is required.");

        RuleFor(c => c.WalletNumber)
            .Null()
            .When(c => c.Method != PaymentMethod.Wallet);

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

    public CreateBookingPaymentIntentCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
    }

    public async Task<Result<BookingPaymentIntentResponse>> Handle(
        CreateBookingPaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
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
                request.WalletNumber,
                booking.PriceSnapshot.TotalPayableAmount,
                booking.PriceSnapshot.Currency,
                request.Billing),
            cancellationToken);

        if (!intent.IsSuccess)
        {
            return Result<BookingPaymentIntentResponse>.Failure(intent.Error);
        }

        // Best-effort compensation: if recording fails after the gateway call,
        // the orphaned Paymob order is settled by ops (no charge without a payment key usage).
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
                intent.Value.IframeUrl,
                intent.Value.WalletRedirectUrl));
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

        // Late payment: the acceptance window closed while the gateway was processing.
        if (booking.Status == BookingStatus.PendingCaregiverApproval
            && request.UtcNow > booking.AcceptanceDeadlineUtc)
        {
            booking.Expire(request.UtcNow);

            Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                paymobTransactionId,
                transaction.Amount,
                cancellationToken);

            if (refund.IsSuccess)
            {
                booking.MarkRefunded(refund.Value, request.UtcNow);
                outcome = "PaidExpired";
            }
            else
            {
                outcome = "PaidExpiredRefundPending";
            }
        }
        else
        {
            outcome = "Paid";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<ConfirmBookingPaymentResponse>.Success(
            new ConfirmBookingPaymentResponse(booking.Id.Value, outcome));
    }
}