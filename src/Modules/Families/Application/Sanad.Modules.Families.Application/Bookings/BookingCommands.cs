using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Bookings;

// ----------------------------- Responses -----------------------------

public sealed record BookingCheckoutResponse(
    Guid BookingId,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency,
    string PaymentClientSecret);

public sealed record FamilyBookingListItemResponse(
    Guid Id,
    Guid CaregiverId,
    string CaregiverArabicName,
    string CaregiverEnglishName,
    string? CaregiverAvatarUrl,
    string SeniorArabicName,
    string SeniorEnglishName,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency);

// --------------------------- Checkout Command -------------------------

public sealed record CreateBookingCheckoutCommand(
    UserId UserId,
    ElderlyId ElderlyId,
    CaregiverId CaregiverId,
    CaregiverType CaregiverType,
    BookingShiftType ShiftType,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ServiceAddress,
    string? SpecialInstructions,
    decimal BaseCaregiverFee,
    DateOnly CurrentDate,
    DateTime UtcNow) : ICommand<BookingCheckoutResponse>;

public sealed class CreateBookingCheckoutCommandHandler : ICommandHandler<CreateBookingCheckoutCommand, BookingCheckoutResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public CreateBookingCheckoutCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BookingCheckoutResponse>> Handle(
        CreateBookingCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve Family & Verify Member Role
        var family = await _dbContext.Families
            .Include(f => f.Members)
            .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

        if (family is null)
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
        }

        FamilyRole? role = family.GetRole(request.UserId);
        if (role is not (FamilyRole.Owner or FamilyRole.Editor))
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.UnauthorizedRole", "Viewers are not permitted to create bookings."));
        }

        // 2. Verify Elderly belongs to this Family
        bool elderlyExists = await _dbContext.Elderlies
            .AnyAsync(e => e.Id == request.ElderlyId && e.FamilyId == family.Id, cancellationToken);

        if (!elderlyExists)
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.ElderlyNotFound", "Elderly dependent does not belong to this family."));
        }

        // 3. Verify Caregiver has no conflicting active bookings
        bool hasConflict = await _dbContext.Bookings.AnyAsync(
            b => b.CaregiverId == request.CaregiverId &&
                 b.BookingDate == request.BookingDate &&
                 (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress) &&
                 ((request.StartTime >= b.StartTime && request.StartTime < b.EndTime) ||
                  (request.EndTime > b.StartTime && request.EndTime <= b.EndTime)),
            cancellationToken);

        if (hasConflict)
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.ScheduleConflict", "Caregiver is already booked for this time slot."));
        }

        // 4. Calculate Price Snapshot (15% platform fee)
        BookingPriceSnapshot priceSnapshot = BookingPriceSnapshot.Calculate(
            request.BaseCaregiverFee,
            15.00m);

        // 5. Create Booking Aggregate
        Booking booking = Booking.Create(
            family.Id,
            request.UserId,
            request.ElderlyId,
            request.CaregiverId,
            request.CaregiverType,
            request.ShiftType,
            request.BookingDate,
            request.StartTime,
            request.EndTime,
            request.ServiceAddress,
            request.SpecialInstructions,
            priceSnapshot,
            request.CurrentDate,
            request.UtcNow);

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);

        string paymentClientSecret = $"paymob_sim_secret_{booking.Id.Value}";

        return Result<BookingCheckoutResponse>.Success(
            new BookingCheckoutResponse(
                booking.Id.Value,
                booking.Status,
                priceSnapshot.TotalPayableAmount,
                priceSnapshot.Currency,
                paymentClientSecret));
    }
}

// ------------------------- Payment Webhook/Confirm -----------------------

public sealed record ConfirmBookingPaymentCommand(
    BookingId BookingId,
    string PaymobOrderId,
    string PaymobTransactionId,
    DateTime UtcNow) : ICommand;

public sealed class ConfirmBookingPaymentCommandHandler : ICommandHandler<ConfirmBookingPaymentCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public ConfirmBookingPaymentCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        ConfirmBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
        }

        booking.MarkAsPaid(request.PaymobOrderId, request.PaymobTransactionId, request.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ------------------------- Cancel Booking Command -----------------------

public sealed record CancelBookingCommand(
    BookingId BookingId,
    UserId UserId,
    string Reason,
    DateTime UtcNow) : ICommand;

public sealed class CancelBookingCommandHandler : ICommandHandler<CancelBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CancelBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
        }

        booking.CancelByFamily(request.Reason, request.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}