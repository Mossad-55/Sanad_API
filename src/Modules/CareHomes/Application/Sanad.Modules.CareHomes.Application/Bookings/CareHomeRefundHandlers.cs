using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record CancelFamilyCareHomeBookingCommand(UserId Actor, FamilyId FamilyId, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record CancelOwnerCareHomeBookingCommand(UserId Actor, Guid BookingId, string Reason, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RetryCareHomeRefundCommand(UserId Actor, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RecordCareHomeRefundCompletedCommand(UserId Actor, Guid BookingId, string Reference, string Reason, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record CompleteCareHomeRefundCallbackCommand(long TransactionId, long? ParentTransactionId, bool IsRefunded, DateTime UtcNow) : ICommand;

public static class CareHomeRefundProcessor
{
    public static async Task<Result> InitiateAsync(
        ICareHomesDbContext db,
        IPaymobClient paymob,
        CareHomeBooking booking,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (!booking.TryClaimRefund(utcNow))
            return Result.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The refund is not ready to be submitted."));

        await db.SaveChangesAsync(cancellationToken);
        if (booking.PaymobTransactionId is null)
        {
            booking.MarkRefundFailed("No Paymob transaction is stored for this paid booking.", utcNow);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The paid booking has no stored Paymob transaction."));
        }
        Result<string?> result = await paymob.RefundPaymentAsync(
            booking.PaymobTransactionId.Value.ToString(),
            booking.RefundAmount!.Value,
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "Paymob.RefundRejected")
            {
                booking.MarkRefundFailed(result.Error.Code, utcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
            return Result.Failure(result.Error);
        }

        booking.MarkRefundInitiated(result.Value, utcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class CancelFamilyCareHomeBookingHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<CancelFamilyCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(CancelFamilyCareHomeBookingCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(
            x => x.Id == request.BookingId && x.FamilyId == request.FamilyId, ct);
        if (booking is null) return NotFound();

        try
        {
            decimal refundAmount = booking.CancelByFamily(request.UtcNow);
            await db.SaveChangesAsync(ct);
            Result futureExtensions = await CareHomeFutureExtensionRefunds.ProcessAsync(
                db, paymob, booking, CareHomeFutureExtensionRefunds.CairoDate(request.UtcNow), request.UtcNow, ct);
            if (refundAmount > 0m)
            {
                Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
                if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            }
            if (futureExtensions.IsFailure) return Result<CareHomeBookingResponse>.Failure(futureExtensions.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while cancellation was being processed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class CancelOwnerCareHomeBookingHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<CancelOwnerCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(CancelOwnerCareHomeBookingCommand request, CancellationToken ct)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == request.Actor, ct);
        CareHomeBooking? booking = facility is null ? null : await db.Bookings.SingleOrDefaultAsync(
            x => x.Id == request.BookingId && x.FacilityId == facility.Id, ct);
        if (booking is null) return NotFound();

        try
        {
            booking.CancelByFacility(request.Reason, request.UtcNow);
            await db.SaveChangesAsync(ct);
            Result futureExtensions = await CareHomeFutureExtensionRefunds.ProcessAsync(
                db, paymob, booking, CareHomeFutureExtensionRefunds.CairoDate(request.UtcNow), request.UtcNow, ct);
            Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
            if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            if (futureExtensions.IsFailure) return Result<CareHomeBookingResponse>.Failure(futureExtensions.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while cancellation was being processed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class RetryCareHomeRefundHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<RetryCareHomeRefundCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RetryCareHomeRefundCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null) return NotFound();
        if (booking.Status != CareHomeBookingStatus.RefundPending || booking.RefundStatus != CareHomeRefundStatus.Failed)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "Only a failed refund can be retried."));

        try
        {
            Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
            if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The refund changed while retry was being claimed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class RecordCareHomeRefundCompletedHandler(ICareHomesDbContext db)
    : ICommandHandler<RecordCareHomeRefundCompletedCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RecordCareHomeRefundCompletedCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
        try
        {
            booking.MarkRefundManuallyCompleted(request.Reference, request.Reason, request.Actor, request.UtcNow);
            await db.SaveChangesAsync(ct);
            return CheckoutHandler.Map(booking);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while refund completion was being recorded."));
        }
    }
}

public sealed class CompleteCareHomeRefundCallbackHandler(ICareHomesDbContext db)
    : ICommandHandler<CompleteCareHomeRefundCallbackCommand>
{
    public async Task<Result> Handle(CompleteCareHomeRefundCallbackCommand request, CancellationToken ct)
    {
        if (!request.IsRefunded) return Result.Success();
        string refundReference = request.TransactionId.ToString();
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(
            x => x.RefundReference == refundReference
                || (request.ParentTransactionId.HasValue && x.PaymobTransactionId == request.ParentTransactionId)
                || x.PaymobTransactionId == request.TransactionId, ct);
        if (booking is null) return Result.Success();
        if (booking.MarkRefundCompletedFromProviderCallback(refundReference, request.ParentTransactionId, request.UtcNow))
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Result.Success(); }
        }
        return Result.Success();
    }
}
