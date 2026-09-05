using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Bookings;

// ============================= Accept Booking =============================

public sealed record CaregiverAcceptBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverAcceptBookingCommandHandler : ICommandHandler<CaregiverAcceptBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverAcceptBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverAcceptBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            try
            {
                booking.AcceptByCaregiver(request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ============================= Decline Booking =============================

public sealed record CaregiverDeclineBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    string Reason,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverDeclineBookingCommandHandler : ICommandHandler<CaregiverDeclineBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;

    public CaregiverDeclineBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
    }

    public async Task<Result> Handle(
        CaregiverDeclineBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            try
            {
                booking.DeclineByCaregiver(request.Reason, request.UtcNow);

                PaymentTransaction? paidTransaction = booking.PaymentTransactions.FirstOrDefault(
                    t => t.Status == PaymentTransactionStatus.Succeeded
                        && t.PaymobTransactionId is not null);

                if (paidTransaction is not null)
                {
                    Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                        paidTransaction.PaymobTransactionId!,
                        paidTransaction.Amount,
                        cancellationToken);

                    if (refund.IsSuccess)
                    {
                        booking.MarkRefunded(refund.Value, request.UtcNow);
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ============================= Start Visit =============================

public sealed record CaregiverStartBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverStartBookingCommandHandler : ICommandHandler<CaregiverStartBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverStartBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverStartBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            try
            {
                booking.StartVisit(request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ============================= Complete Visit =============================

public sealed record CaregiverCompleteBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    string? Notes,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverCompleteBookingCommandHandler : ICommandHandler<CaregiverCompleteBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverCompleteBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverCompleteBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            try
            {
                booking.CompleteVisit(request.Notes, request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}