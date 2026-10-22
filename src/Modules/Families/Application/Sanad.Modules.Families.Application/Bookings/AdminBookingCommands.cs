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

            BookingCancellationFact? fact = await _dbContext.BookingCancellationFacts
                .AsNoTracking()
                .SingleOrDefaultAsync(f => f.BookingId == request.BookingId, cancellationToken);

            if (fact is not null && fact.RefundEntitlement == BookingRefundEntitlement.NoRefundDue)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error(
                        "Bookings.NoRefundDue",
                        "No refund is due for this cancellation; the policy retained the amount."));
            }

            // Decision #17: an unconfirmed attempt — or a claim that is still in flight because the
            // process died before recording its outcome — blocks even the operator's retry until a
            // reconciliation clears it. The retry is exactly what must not happen blindly, and an
            // interrupted attempt is uncertain rather than a failure to be re-sent. This is checked
            // first because such a booking resolves to Pending, not Failed.
            if (booking.IsRefundOutcomeAmbiguous || booking.IsRefundAttemptInFlight)
            {
                return Result<BookingDetailResponse>.Failure(
                    BookingRefundSettlementErrors.AmbiguousOutcome);
            }

            // Eligibility before budget: a booking whose refund is not retryable at all (e.g. an
            // unpaid cancellation) must report the state reason, not the money reason, so the
            // operator is not told to reconcile an amount for a booking that can never be refunded.
            if (BookingRefundStates.Resolve(booking) != BookingRefundState.Failed)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error(
                        "Bookings.RefundNotEligible",
                        "Only paid cancelled, declined, or expired bookings with a failed refund can be retried."));
            }

            // Decision #4: cumulative refunds never exceed the captured funds less previous
            // refunds. A booking with nothing left to refund is refused here instead of being
            // pushed at the gateway a second time.
            if (booking.RefundableCeilingMinorUnits() <= 0)
            {
                return Result<BookingDetailResponse>.Failure(
                    BookingRefundSettlementErrors.AwardExceedsCeiling);
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

            // The retry sends the authoritative outstanding award, never a recomputed figure:
            //
            //  * A revision-2 fact stores the exact award the policy decided, in piastres. What is
            //    still owed is that award less the money already settled against this booking,
            //    capped by the remaining captured budget. A retry of a failed 50% award therefore
            //    sends 50% — never the whole capture — and an award that already settled sends
            //    nothing at all, so a repeated retry cannot pay twice.
            //  * A revision-1 fact is amount-free by design (the whole captured amount, or
            //    nothing), so its retry keeps the legacy semantics: the remaining ceiling.
            //  * A revision-2 fact with no recorded award is incomplete evidence. It fails closed
            //    rather than falling back to a full-capture refund the policy never granted.
            long ceiling = booking.RefundableCeilingMinorUnits();

            long outstandingMinorUnits = BookingRefundRetryAmount.Resolve(
                fact?.PolicyVersion,
                fact?.AwardedMinorUnits,
                ceiling,
                booking.RefundedMinorUnits,
                out Error? retryError);

            if (retryError is not null)
            {
                return Result<BookingDetailResponse>.Failure(retryError);
            }

            if (outstandingMinorUnits <= 0)
            {
                return Result<BookingDetailResponse>.Failure(
                    BookingRefundSettlementErrors.AwardExceedsCeiling);
            }

            // Claim the attempt and COMMIT it before the provider call: the committed claim is what
            // makes this retry exclusive, so a competing request with a stale concurrency token
            // fails its own save and never reaches the gateway.
            Guid claimId = Guid.NewGuid();
            booking.ClaimRefundAttempt(claimId, outstandingMinorUnits, request.UtcNow);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<BookingDetailResponse>.Failure(
                    new Error(
                        "Bookings.Cancel.AlreadyProcessed",
                        "This booking's refund was claimed by another request."));
            }

            decimal outstanding = BookingRefundSettlementExecutor.ToMajorUnits(outstandingMinorUnits);

            Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                transactionId,
                outstanding,
                cancellationToken);

            if (!refund.IsSuccess)
            {
                // An unconfirmed outcome is recorded on the booking rather than left as a plain
                // failure, so the next attempt is refused until it is reconciled. A definitive
                // refusal proves nothing moved, so the claim is released and a bounded retry stays
                // possible.
                if (string.Equals(refund.Error?.Code, "Paymob.RefundOutcomeUnknown", StringComparison.Ordinal))
                {
                    booking.MarkRefundOutcomeAmbiguous(
                        transactionId,
                        refund.Error!.Code,
                        request.UtcNow,
                        outstandingMinorUnits);
                }
                else
                {
                    booking.ReleaseRefundAttempt(request.UtcNow);
                }

                if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                        _dbContext,
                        booking.Id,
                        0,
                        cancellationToken))
                {
                    return Result<BookingDetailResponse>.Failure(
                        BookingRefundSettlementErrors.SettlementIncomplete);
                }

                return Result<BookingDetailResponse>.Failure(refund.Error!);
            }

            booking.RecordRefundedAmount(outstandingMinorUnits, request.UtcNow);
            booking.MarkRefunded(refund.Value, request.UtcNow);
            booking.ReleaseRefundAttempt(request.UtcNow);

            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    outstandingMinorUnits,
                    cancellationToken))
            {
                return Result<BookingDetailResponse>.Failure(
                    BookingRefundSettlementErrors.SettlementIncomplete);
            }

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
