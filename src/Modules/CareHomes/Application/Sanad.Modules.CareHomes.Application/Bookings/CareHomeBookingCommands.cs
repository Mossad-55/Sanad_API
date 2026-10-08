using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record CareHomeBookingResponse(Guid Id, Guid FacilityId, Guid ElderlyId, Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate, DateTime EarliestArrivalUtc, CareHomeBookingStatus Status, CareHomeBookingPaymentStatus PaymentStatus, decimal BaseAmount, decimal PlatformFeeAmount, decimal TaxAmount, decimal TotalAmount, string Currency, DateTime? HoldUntilUtc, string? DecisionReason, Guid? AssignedRoomId = null, Guid? AssignedBedId = null, DateTime? ActualCheckInOnUtc = null, DateTime? ActualCheckOutOnUtc = null, DateTime? FamilyCheckInConfirmedOnUtc = null, CareHomeRefundStatus RefundStatus = CareHomeRefundStatus.None, decimal? RefundAmount = null, string? RefundReference = null, DateTime? RefundCompletedOnUtc = null, Guid? RefundCompletedBy = null, string? RefundFailureReason = null);
public sealed record CareHomeBookingPaymentIntentResponse(Guid BookingId, string MerchantReference, string PaymobOrderId, string IntentionOrderId, string ClientSecret, string PublicKey);
public sealed record CareHomeBookingListItem(Guid Id, Guid FacilityId, Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate, CareHomeBookingStatus Status, CareHomeBookingPaymentStatus PaymentStatus, decimal TotalAmount, string Currency, string ElderlyEnglishName, Guid? AssignedRoomId = null, Guid? AssignedBedId = null, DateTime? ActualCheckInOnUtc = null, DateTime? ActualCheckOutOnUtc = null, DateTime? FamilyCheckInConfirmedOnUtc = null, CareHomeRefundStatus RefundStatus = CareHomeRefundStatus.None, decimal? RefundAmount = null);

public sealed record CheckoutCareHomeBookingCommand(UserId Actor, FamilyId FamilyId, ElderlyIntakeResolution Intake, Guid FacilityId, Guid RoomTypeId, DateOnly StartDate, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record ListFamilyCareHomeBookingsQuery(UserId Actor, FamilyId FamilyId) : IQuery<IReadOnlyList<CareHomeBookingListItem>>;
public sealed record GetFamilyCareHomeBookingQuery(UserId Actor, FamilyId FamilyId, Guid BookingId, DateTime UtcNow) : IQuery<CareHomeBookingResponse>;
public sealed record CreateCareHomePaymentIntentCommand(UserId Actor, FamilyId FamilyId, Guid BookingId, PaymentMethod Method, PaymobBillingData Billing, DateTime UtcNow) : ICommand<CareHomeBookingPaymentIntentResponse>;
public sealed record ListOwnerCareHomeBookingsQuery(UserId Actor, DateTime UtcNow) : IQuery<IReadOnlyList<CareHomeBookingListItem>>;
public sealed record DecideCareHomeBookingCommand(UserId Actor, Guid BookingId, bool Accept, string? Reason, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record ConfirmCareHomePaymentCommand(string MerchantReference, long TransactionId, long AmountCents, bool Success, bool Pending, DateTime UtcNow) : ICommand;
public interface ICareHomeBookingReservationGuard
{
    // The lock key is facility + room type. It intentionally excludes the date so
    // overlapping calendar-month requests cannot bypass serialization.
    Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken);
}
public sealed class CareHomeCapacityConflictException : Exception { }

public sealed class CheckoutValidator : AbstractValidator<CheckoutCareHomeBookingCommand>
{ public CheckoutValidator() { RuleFor(x => x.FacilityId).NotEqual(Guid.Empty); RuleFor(x => x.RoomTypeId).NotEqual(Guid.Empty); RuleFor(x => x.StartDate).NotEqual(default(DateOnly)); } }

public sealed class CheckoutHandler(ICareHomesDbContext db, IPlatformChargeRuleReader charges, ICareHomeOccupancyProvider occupancy, ICareHomeBookingReservationGuard reservationGuard, CareHomeBookingTiming? timing = null) : ICommandHandler<CheckoutCareHomeBookingCommand, CareHomeBookingResponse>
{
    private readonly CareHomeBookingTiming _timing = timing ?? CareHomeBookingTiming.Default;
    public async Task<Result<CareHomeBookingResponse>> Handle(CheckoutCareHomeBookingCommand r, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Revisions).Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(r.FacilityId) && x.Status == CareHomeStatus.Approved, ct);
        var type = await db.RoomTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.RoomTypeId && x.FacilityId == new CareHomeId(r.FacilityId) && !x.IsArchived, ct);
        if (facility is null || type is null || !CareHomePublicEligibility.IsEligible(facility, CareHomePublicEligibility.CairoDate(r.UtcNow)))
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "The selected care home or room type was not found."));
        DateOnly end = r.StartDate.AddMonths(1);
        try { return await reservationGuard.ExecuteAsync(facility.Id, type.Id, r.StartDate, async () =>
        {
            var effective = await charges.GetEffectiveAsync(r.UtcNow, ct);
            if (effective is null) return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));
            var active = await occupancy.GetActiveAsync(facility.Id, ct);
            var availability = CareHomeAvailabilityCalculator.Calculate(
                await db.RoomTypes.AsNoTracking().Where(x => x.FacilityId == facility.Id).ToListAsync(ct),
                await db.Rooms.AsNoTracking().Where(x => x.FacilityId == facility.Id).ToListAsync(ct),
                await db.Beds.AsNoTracking().Where(x => x.FacilityId == facility.Id).ToListAsync(ct),
                await db.MaintenanceBlocks.AsNoTracking().Where(x => x.FacilityId == facility.Id).ToListAsync(ct),
                active, [], r.StartDate, end);
            var requested = availability.SingleOrDefault(x => x.RoomTypeId == type.Id);
            bool hasCapacity = type.AllocationMode is CareHomeAllocationMode.Shared ? requested?.AvailableBeds > 0 : requested?.AvailableRooms > 0;
            if (hasCapacity is not true) return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.CapacityConflict", "The selected room type is no longer available."));
            var snapshot = Sanad.BuildingBlocks.Domain.ValueObjects.BookingPriceSnapshot.Calculate(type.MonthlyPriceEgp, effective.PlatformFeeRatePercentage, effective.TaxRatePercentage, effective.Version);
            string medicalJson = JsonSerializer.Serialize(r.Intake.MedicalProfile);
            CareHomeBooking booking;
            try { booking = CareHomeBooking.Create(facility.Id, r.Actor, r.FamilyId, r.Intake.ElderlyId, type.Id, r.StartDate, snapshot.BaseCaregiverFee, snapshot.PlatformFeeAmount, snapshot.TaxAmount, snapshot.PlatformChargeRuleVersion!.Value, r.Intake.ArabicFullName, r.Intake.EnglishFullName, r.Intake.Age, medicalJson, r.Intake.ResponsibleContact.Name, r.Intake.ResponsibleContact.PhoneNumber, r.Intake.ResponsibleContact.Relationship, r.Intake.CareNeedsNotes, r.UtcNow, _timing.CheckoutHoldDuration); }
            catch (ArgumentException ex) { return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.Invalid", ex.Message)); }
            db.Bookings.Add(booking); await db.SaveChangesAsync(ct); return Result<CareHomeBookingResponse>.Success(Map(booking));
        }, ct); }
        catch (CareHomeCapacityConflictException) { return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.CapacityConflict", "The selected room type is no longer available.")); }
        catch (DbUpdateConcurrencyException) { return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.CapacityConflict", "The selected room type is no longer available.")); }
    }
    internal static CareHomeBookingResponse Map(CareHomeBooking x) => new(x.Id, x.FacilityId.Value, x.ElderlyId.Value, x.RoomTypeId, x.StartDate, x.EndDate, x.EarliestArrivalUtc, x.Status, x.PaymentStatus, x.BaseAmount, x.PlatformFeeAmount, x.TaxAmount, x.TotalAmount, "EGP", x.Status == CareHomeBookingStatus.PendingPayment ? x.CheckoutHoldUntilUtc : x.DecisionHoldUntilUtc, x.DecisionReason, x.AssignedRoomId, x.AssignedBedId, x.ActualCheckInOnUtc, x.ActualCheckOutOnUtc, x.FamilyCheckInConfirmedOnUtc, x.RefundStatus, x.RefundAmount, x.RefundReference, x.RefundCompletedOnUtc, x.RefundCompletedBy?.Value, x.RefundFailureReason);
}

public sealed class FamilyListHandler(ICareHomesDbContext db) : IQueryHandler<ListFamilyCareHomeBookingsQuery, IReadOnlyList<CareHomeBookingListItem>>
{ public async Task<Result<IReadOnlyList<CareHomeBookingListItem>>> Handle(ListFamilyCareHomeBookingsQuery r, CancellationToken ct) => await db.Bookings.AsNoTracking().Where(x => x.FamilyId == r.FamilyId).OrderByDescending(x => x.CreatedOnUtc).Select(x => new CareHomeBookingListItem(x.Id, x.FacilityId.Value, x.RoomTypeId, x.StartDate, x.EndDate, x.Status, x.PaymentStatus, x.TotalAmount, "EGP", x.ElderlyEnglishName, x.AssignedRoomId, x.AssignedBedId, x.ActualCheckInOnUtc, x.ActualCheckOutOnUtc, x.FamilyCheckInConfirmedOnUtc, x.RefundStatus, x.RefundAmount)).ToListAsync(ct); }
public sealed class FamilyDetailHandler(ICareHomesDbContext db) : IQueryHandler<GetFamilyCareHomeBookingQuery, CareHomeBookingResponse>
{ public async Task<Result<CareHomeBookingResponse>> Handle(GetFamilyCareHomeBookingQuery r, CancellationToken ct) { var x = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FamilyId == r.FamilyId, ct); if (x is null) return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found.")); x.ExpireIfNeeded(r.UtcNow); return CheckoutHandler.Map(x); } }

public sealed class PaymentIntentHandler(ICareHomesDbContext db, IPaymobClient paymob) : ICommandHandler<CreateCareHomePaymentIntentCommand, CareHomeBookingPaymentIntentResponse>
{
    public async Task<Result<CareHomeBookingPaymentIntentResponse>> Handle(CreateCareHomePaymentIntentCommand r, CancellationToken ct)
    {
        var x = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FamilyId == r.FamilyId, ct);
        if (x is null) return Result<CareHomeBookingPaymentIntentResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
        x.ExpireIfNeeded(r.UtcNow);
        if (x.PaymobOrderId is not null) return new CareHomeBookingPaymentIntentResponse(x.Id, x.MerchantReference, x.PaymobOrderId, x.IntentionOrderId!, x.PaymentClientSecret!, x.PaymentPublicKey!);
        if (x.Status != CareHomeBookingStatus.PendingPayment || !x.TryClaimPaymentIntent(r.UtcNow)) return Result<CareHomeBookingPaymentIntentResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "A payment intent is already being created or the booking is no longer payable."));
        await db.SaveChangesAsync(ct);
        var result = await paymob.CreatePaymentIntentAsync(new PaymobPaymentIntentInput(new BookingId(x.Id), r.Method, x.TotalAmount, "EGP", r.Billing, x.MerchantReference), ct);
        if (result.IsFailure) { x.ReleasePaymentIntentClaim(r.UtcNow); await db.SaveChangesAsync(ct); return Result<CareHomeBookingPaymentIntentResponse>.Failure(result.Error); }
        x.SetPaymentIntent((int)r.Method, result.Value.PaymobOrderId, result.Value.IntentionOrderId, result.Value.ClientSecret, result.Value.PublicKey, r.UtcNow);
        await db.SaveChangesAsync(ct);
        return new CareHomeBookingPaymentIntentResponse(x.Id, x.MerchantReference, result.Value.PaymobOrderId, result.Value.IntentionOrderId, result.Value.ClientSecret, result.Value.PublicKey);
    }
}

public sealed class OwnerListHandler(ICareHomesDbContext db) : IQueryHandler<ListOwnerCareHomeBookingsQuery, IReadOnlyList<CareHomeBookingListItem>>
{ public async Task<Result<IReadOnlyList<CareHomeBookingListItem>>> Handle(ListOwnerCareHomeBookingsQuery r, CancellationToken ct) { var f = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct); if (f is null) return Result<IReadOnlyList<CareHomeBookingListItem>>.Failure(new Error("CareHomes.Bookings.NotFound", "Facility was not found.")); var xs = await db.Bookings.AsNoTracking().Where(x => x.FacilityId == f.Id && x.PaymentStatus != CareHomeBookingPaymentStatus.Pending && x.PaymentStatus != CareHomeBookingPaymentStatus.Failed).OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct); return xs.Select(x => new CareHomeBookingListItem(x.Id, x.FacilityId.Value, x.RoomTypeId, x.StartDate, x.EndDate, x.Status, x.PaymentStatus, x.TotalAmount, "EGP", x.ElderlyEnglishName, x.AssignedRoomId, x.AssignedBedId, x.ActualCheckInOnUtc, x.ActualCheckOutOnUtc, x.FamilyCheckInConfirmedOnUtc, x.RefundStatus, x.RefundAmount)).ToArray(); } }
public sealed class DecideHandler(ICareHomesDbContext db, IPaymobClient paymob) : ICommandHandler<DecideCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(DecideCareHomeBookingCommand r, CancellationToken ct)
    {
        var f = await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct);
        var x = f is null ? null : await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FacilityId == f.Id, ct);
        if (x is null) return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
        try
        {
            if (r.Accept)
            {
                x.Accept(r.UtcNow);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                x.Reject(r.Reason ?? string.Empty, r.UtcNow);
                await db.SaveChangesAsync(ct);
                var refund = await CareHomeRefundProcessor.InitiateAsync(db, paymob, x, r.UtcNow, ct);
                if (refund.IsFailure) return Result<CareHomeBookingResponse>.Failure(refund.Error);
            }
            return CheckoutHandler.Map(x);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while the facility decision was being processed."));
        }
    }
}

public sealed class ConfirmPaymentHandler(ICareHomesDbContext db, IPaymobClient paymob, CareHomeBookingTiming? timing = null) : ICommandHandler<ConfirmCareHomePaymentCommand>
{
    private readonly CareHomeBookingTiming _timing = timing ?? CareHomeBookingTiming.Default;
    public async Task<Result> Handle(ConfirmCareHomePaymentCommand r, CancellationToken ct)
    {
        var x = await db.Bookings.SingleOrDefaultAsync(x => x.MerchantReference == r.MerchantReference, ct);
        if (x is null) return Result.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
        if (r.AmountCents != decimal.ToInt64(decimal.Round(x.TotalAmount * 100m)))
            return Result.Failure(new Error("Paymob.AmountMismatch", "Payment amount does not match the booking."));

        try
        {
            ApplyPaymentResult(x, r);
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "CareHomes.Bookings.PaymentConflict")
        {
            return PaymentConflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Re-read the winning state before acknowledging a provider callback. A success
            // racing a failure must still be recorded (and refunded if its hold has ended),
            // while a different successful transaction must remain a visible conflict.
            await db.Bookings.Entry(x).ReloadAsync(ct);
            try
            {
                if (x.PaymentStatus != CareHomeBookingPaymentStatus.Paid || x.PaymobTransactionId != r.TransactionId)
                {
                    ApplyPaymentResult(x, r);
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message == "CareHomes.Bookings.PaymentConflict")
            {
                return PaymentConflict();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(new Error("CareHomes.Bookings.PaymentConflict", "The booking payment changed concurrently; retry the provider callback."));
            }
        }

        if (r.Success && x.Status == CareHomeBookingStatus.RefundPending)
            return await CareHomeRefundProcessor.InitiateAsync(db, paymob, x, r.UtcNow, ct);
        return Result.Success();
    }

    private void ApplyPaymentResult(CareHomeBooking booking, ConfirmCareHomePaymentCommand request)
    {
        if (request.Success) booking.MarkPaid(request.TransactionId, request.UtcNow, _timing.DecisionHoldDuration);
        else if (!request.Pending) booking.MarkPaymentFailed(request.UtcNow);
    }

    private static Result PaymentConflict() =>
        Result.Failure(new Error("CareHomes.Bookings.PaymentConflict", "A different payment transaction is already recorded."));
}
