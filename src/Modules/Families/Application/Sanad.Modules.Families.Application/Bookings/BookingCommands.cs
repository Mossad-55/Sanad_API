using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
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
    string? CaregiverArabicName,
    string? CaregiverEnglishName,
    string? CaregiverAvatarUrl,
    string? SeniorArabicName,
    string? SeniorEnglishName,
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
    BookingShiftType ShiftType,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ServiceAddress,
    string? SpecialInstructions,
    DateOnly CurrentDate,
    DateTime UtcNow) : ICommand<BookingCheckoutResponse>;

public sealed class CreateBookingCheckoutCommandHandler : ICommandHandler<CreateBookingCheckoutCommand, BookingCheckoutResponse>
{
    private const int AcceptanceWindowHours = 24;
    private const decimal PlatformCommissionPercentage = 15.00m;

    private readonly IFamiliesDbContext _dbContext;
    private readonly ICaregiverBookingPricing _caregiverBookingPricing;

    public CreateBookingCheckoutCommandHandler(
        IFamiliesDbContext dbContext,
        ICaregiverBookingPricing caregiverBookingPricing)
    {
        _dbContext = dbContext;
        _caregiverBookingPricing = caregiverBookingPricing;
    }

    public async Task<Result<BookingCheckoutResponse>> Handle(
        CreateBookingCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        try
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

            // 3. Verify Caregiver has no conflicting booking (pending bookings reserve the slot too)
            bool hasConflict = await _dbContext.Bookings.AnyAsync(
                b => b.CaregiverId == request.CaregiverId &&
                     b.BookingDate == request.BookingDate &&
                     (b.Status == BookingStatus.PendingPayment ||
                      b.Status == BookingStatus.PendingCaregiverApproval ||
                      b.Status == BookingStatus.Confirmed ||
                      b.Status == BookingStatus.InProgress) &&
                     request.StartTime < b.EndTime &&
                     request.EndTime > b.StartTime,
                cancellationToken);

            if (hasConflict)
            {
                return Result<BookingCheckoutResponse>.Failure(
                    new Error("Bookings.ScheduleConflict", "Caregiver is already booked for this time slot."));
            }

            // 4. Server-side pricing — never trust a client-supplied fee (Section 13.1)
            Result<CaregiverBookingPrice> price = await _caregiverBookingPricing.GetBookingPriceAsync(
                request.CaregiverId,
                request.ShiftType,
                request.StartTime,
                request.EndTime,
                cancellationToken);

            if (!price.IsSuccess)
            {
                return Result<BookingCheckoutResponse>.Failure(price.Error);
            }

            // 5. Acceptance window = min(now + 24h, booking start)
            DateTime bookingStartUtc = request.BookingDate.ToDateTime(request.StartTime);
            DateTime acceptanceDeadline =
                request.UtcNow.AddHours(AcceptanceWindowHours) <= bookingStartUtc
                    ? request.UtcNow.AddHours(AcceptanceWindowHours)
                    : bookingStartUtc;

            // 6. Immutable price snapshot (family pays base + platform fee)
            BookingPriceSnapshot priceSnapshot = BookingPriceSnapshot.Calculate(
                price.Value.BaseFee,
                PlatformCommissionPercentage);

            // 7. Create Booking Aggregate
            Booking booking = Booking.Create(
                family.Id,
                request.UserId,
                request.ElderlyId,
                request.CaregiverId,
                price.Value.CaregiverType,
                request.ShiftType,
                request.BookingDate,
                request.StartTime,
                request.EndTime,
                request.ServiceAddress,
                request.SpecialInstructions,
                priceSnapshot,
                acceptanceDeadline,
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
        catch (DomainException exception)
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ------------------------- Payment Webhook/Confirm -----------------------
// DEPLOY GATE (2026-09-05 ruling): this command must remain UNEXPOSED over
// HTTP until the Paymob slice provides HMAC-verified, idempotent confirmation.

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
        try
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
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// --------------------------- Cancel Command ------------------------------

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
        try
        {
            var family = await _dbContext.Families
                .AsNoTracking()
                .Include(f => f.Members)
                .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

            if (family is null)
            {
                return Result.Failure(
                    new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
            }

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
            }

            if (booking.FamilyId != family.Id)
            {
                return Result.Failure(
                    new Error("Bookings.BookingNotInFamily", "Booking was not found in this family."));
            }

            booking.CancelByFamily(request.Reason, request.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}