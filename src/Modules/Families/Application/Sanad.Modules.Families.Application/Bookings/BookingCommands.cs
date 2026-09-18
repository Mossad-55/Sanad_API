using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Bookings;

// ----------------------------- Responses -----------------------------

public sealed record BookingCheckoutResponse(
    Guid BookingId,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency);

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

            return Result<BookingCheckoutResponse>.Success(
                new BookingCheckoutResponse(
                    booking.Id.Value,
                    booking.Status,
                    priceSnapshot.TotalPayableAmount,
                    priceSnapshot.Currency));
        }
        catch (DomainException exception)
        {
            return Result<BookingCheckoutResponse>.Failure(
                new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// --------------------------- Cancel Command ------------------------------

public sealed record CancelBookingCommand(
    BookingId BookingId,
    UserId UserId,
    string? Reason,
    int? ReasonCategory,
    DateTime UtcNow) : ICommand;

public sealed class CancelBookingCommandHandler : ICommandHandler<CancelBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public CancelBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        CancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Resolve family by membership (as before)
            var family = await _dbContext.Families
                .AsNoTracking()
                .Include(f => f.Members)
                .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

            if (family is null)
            {
                return Result.Failure(
                    new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
            }

            // 2. Role gate: only Owner/Editor may cancel (Viewers rejected, same pattern as checkout)
            FamilyRole? role = family.GetRole(request.UserId);
            if (role is not (FamilyRole.Owner or FamilyRole.Editor))
            {
                return Result.Failure(
                    new Error("Bookings.UnauthorizedRole", "Viewers are not permitted to cancel bookings."));
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

            // 3. Feedback shape validation — before any domain mutation
            BookingCancellationFeedback? feedback;

            if (booking.Status == BookingStatus.Confirmed)
            {
                // Accepted: a known category AND a non-blank note are mandatory
                if (request.ReasonCategory is null)
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonCategoryRequired",
                            "A reason category is required to cancel an accepted booking."));
                }

                if (BookingCancellationReasonCategories.TryParse(request.ReasonCategory.Value, out BookingCancellationReasonCategory category) is false)
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonCategoryInvalid",
                            "The supplied reason category is not a defined cancellation reason."));
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonRequired",
                            "A reason note is required to cancel an accepted booking."));
                }

                feedback = BookingCancellationFeedback.Create(category, request.Reason);
            }
            else if (request.ReasonCategory is not null
                && booking.Status is (BookingStatus.PendingPayment or BookingStatus.PendingCaregiverApproval))
            {
                // Pre-acceptance: no category may be invented
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance",
                        "A reason category may not be supplied before the booking is accepted."));
            }
            else
            {
                // Pre-acceptance: optional plain note (blank normalizes to null).
                // Any other status falls through to the policy below, which refuses it.
                feedback = BookingCancellationFeedback.CreateOptionalNote(request.Reason);
            }

            // 4. Policy decision first — every rejection happens on an untouched aggregate
            var input = BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                request.UtcNow,
                feedback);
            var decision = BookingCancellationPolicy.Decide(input);

            // 5. State transition — the aggregate's status guard stays authoritative
            booking.CancelByFamily(feedback?.Note, request.UtcNow);

            // 6. Refund by entitlement: only a full-captured entitlement touches the provider
            switch (decision.RefundEntitlement)
            {
                case BookingRefundEntitlement.FullCapturedRefund:
                {
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

                    break;
                }

                case BookingRefundEntitlement.NoRefundDue:
                    // Policy denial (or nothing captured): no provider call, no status touch.
                    break;

                default:
                    throw new UnreachableException(
                        $"Unexpected refund entitlement '{decision.RefundEntitlement}'.");
            }

            // 7. Record exactly one fact, after the refund attempt so the recorded
            //    decision reflects the final state (immediate-refund success included)
            var fact = BookingCancellationFact.Create(
                booking.Id,
                request.UserId,
                decision);
            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // 8. Single save — booking mutation + fact commit atomically
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (DbUpdateException exception) when (IsFactUniqueViolation(exception))
        {
            // Parallel-cancel race: a fact for this booking was committed first
            return Result.Failure(
                new Error(
                    "Bookings.Cancel.AlreadyProcessed",
                    "This booking cancellation was already processed."));
        }
    }

    /// <summary>
    /// Recognizes the unique violation on <c>families.booking_cancellation_facts</c>
    /// (<c>ux_booking_cancellation_facts_booking</c>). The Application assembly does not reference
    /// Npgsql, so the check reads the inner exception message, where PostgreSQL reports the
    /// constraint name for a unique violation.
    /// </summary>
    private static bool IsFactUniqueViolation(DbUpdateException exception)
    {
        const string constraintName = "ux_booking_cancellation_facts_booking";

        for (Exception? inner = exception.InnerException;
            inner is not null;
            inner = inner.InnerException)
        {
            if (inner.Message.Contains(constraintName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}