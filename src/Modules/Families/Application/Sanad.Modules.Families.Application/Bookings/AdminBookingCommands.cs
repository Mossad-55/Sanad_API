using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record AdminRefundBookingCommand(
    BookingId BookingId,
    DateTime UtcNow) : ICommand<BookingDetailResponse>;

public sealed class AdminRefundBookingCommandHandler
    : ICommandHandler<AdminRefundBookingCommand, BookingDetailResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;

    public AdminRefundBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
    }

    public async Task<Result<BookingDetailResponse>> Handle(
        AdminRefundBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

            if (booking is null)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error("Bookings.NotFound", "Booking not found."));
            }

            if (booking.Status == BookingStatus.Refunded
                || booking.RefundedOnUtc is not null)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error("Bookings.AlreadyRefunded", "This booking is already refunded."));
            }

            if (BookingRefundStates.Resolve(booking) != BookingRefundState.Failed)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error(
                        "Bookings.RefundNotEligible",
                        "Only paid cancelled, declined, or expired bookings with a failed refund can be retried."));
            }

            PaymentTransaction? paidTransaction = booking.PaymentTransactions.FirstOrDefault(
                t => t.Status == PaymentTransactionStatus.Succeeded
                    && t.PaymobTransactionId is not null);

            string? transactionId = paidTransaction?.PaymobTransactionId
                ?? booking.PaymobTransactionId;

            if (string.IsNullOrWhiteSpace(transactionId))
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error(
                        "Bookings.RefundNotEligible",
                        "No Paymob transaction id is stored for this booking."));
            }

            decimal amount = paidTransaction?.Amount
                ?? booking.PriceSnapshot.TotalPayableAmount;

            Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                transactionId,
                amount,
                cancellationToken);

            if (!refund.IsSuccess)
            {
                return Result<BookingDetailResponse>.Failure(refund.Error);
            }

            booking.MarkRefunded(refund.Value, request.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);

            Elderly? elderly = await _dbContext.Elderlies
                .AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == booking.ElderlyId, cancellationToken);

            return Result<BookingDetailResponse>.Success(
                BookingDetailMapper.ToResponse(booking, elderly));
        }
        catch (DomainException exception)
        {
            return Result<BookingDetailResponse>.Failure(
                new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}
