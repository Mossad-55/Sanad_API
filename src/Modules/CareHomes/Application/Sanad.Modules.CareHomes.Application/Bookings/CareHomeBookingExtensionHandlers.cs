using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed class CreateCareHomeBookingExtensionHandler(
    ICareHomesDbContext db,
    IPlatformChargeRuleReader charges,
    ICareHomeOccupancyProvider occupancy,
    ICareHomeBookingReservationGuard reservationGuard,
    CareHomeBookingTiming? timing = null)
    : ICommandHandler<CreateCareHomeBookingExtensionCommand, CareHomeBookingResponse>
{
    private readonly CareHomeBookingTiming _timing = timing ?? CareHomeBookingTiming.Default;

    public async Task<Result<CareHomeBookingResponse>> Handle(CreateCareHomeBookingExtensionCommand request, CancellationToken ct)
    {
        var key = await db.Bookings.AsNoTracking()
            .Where(x => x.Id == request.BookingId && x.FamilyId == request.FamilyId)
            .Select(x => new { x.FacilityId, x.RoomTypeId, x.StartDate })
            .SingleOrDefaultAsync(ct);
        if (key is null) return NotFound();

        try
        {
            return await reservationGuard.ExecuteAsync(key.FacilityId, key.RoomTypeId, key.StartDate, async () =>
            {
                var current = await db.Bookings.SingleOrDefaultAsync(
                    x => x.Id == request.BookingId && x.FamilyId == request.FamilyId, ct);
                if (current is null) return NotFound();
                if (current.Status != CareHomeBookingStatus.Accepted
                    || current.PaymentStatus != CareHomeBookingPaymentStatus.Paid
                    || current.ActualCheckOutOnUtc is not null)
                    return InvalidState();

                Guid rootId = current.ExtensionRootBookingId ?? current.Id;
                if (current.EndDate < CareHomePublicEligibility.CairoDate(request.UtcNow)) return InvalidState();
                var related = await db.Bookings.AsNoTracking()
                    .Where(x => x.Id == rootId || x.ExtensionRootBookingId == rootId)
                    .ToListAsync(ct);
                var latestPaidSegment = related
                    .Where(x => x.Status == CareHomeBookingStatus.Accepted
                        && x.PaymentStatus == CareHomeBookingPaymentStatus.Paid
                        && x.ActualCheckOutOnUtc is null)
                    .OrderByDescending(x => x.EndDate)
                    .FirstOrDefault();
                if (latestPaidSegment?.Id != current.Id) return InvalidState();

                bool pendingExtension = related.Any(x => x.ExtensionRootBookingId == rootId
                    && x.StartDate == current.EndDate
                    && x.IsCapacityActive(request.UtcNow));
                if (pendingExtension) return InvalidState();

                var facility = await db.Facilities.AsNoTracking()
                    .Include(x => x.Revisions).Include(x => x.Documents)
                    .SingleOrDefaultAsync(x => x.Id == current.FacilityId && x.Status == CareHomeStatus.Approved, ct);
                var type = await db.RoomTypes.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == current.RoomTypeId && x.FacilityId == current.FacilityId && !x.IsArchived, ct);
                if (facility is null || type is null
                    || !CareHomePublicEligibility.IsEligible(facility, CareHomePublicEligibility.CairoDate(request.UtcNow)))
                    return NotFound();

                var effective = await charges.GetEffectiveAsync(request.UtcNow, ct);
                if (effective is null)
                    return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));

                DateOnly start = current.EndDate;
                DateOnly end = start.AddMonths(1);
                var active = await occupancy.GetActiveAsync(current.FacilityId, ct);
                var availability = CareHomeAvailabilityCalculator.Calculate(
                    await db.RoomTypes.AsNoTracking().Where(x => x.FacilityId == current.FacilityId).ToListAsync(ct),
                    await db.Rooms.AsNoTracking().Where(x => x.FacilityId == current.FacilityId).ToListAsync(ct),
                    await db.Beds.AsNoTracking().Where(x => x.FacilityId == current.FacilityId).ToListAsync(ct),
                    await db.MaintenanceBlocks.AsNoTracking().Where(x => x.FacilityId == current.FacilityId).ToListAsync(ct),
                    active, [], start, end);
                var roomTypeAvailability = availability.SingleOrDefault(x => x.RoomTypeId == current.RoomTypeId);
                bool hasCapacity = type.AllocationMode == CareHomeAllocationMode.Shared
                    ? roomTypeAvailability?.AvailableBeds > 0
                    : roomTypeAvailability?.AvailableRooms > 0;
                if (!hasCapacity)
                    return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.CapacityConflict", "The selected room type is no longer available for the extension period."));

                var snapshot = Sanad.BuildingBlocks.Domain.ValueObjects.BookingPriceSnapshot.Calculate(
                    type.MonthlyPriceEgp, effective.PlatformFeeRatePercentage, effective.TaxRatePercentage, effective.Version);
                CareHomeBooking extension;
                try
                {
                    extension = CareHomeBooking.CreateExtension(current, request.Actor,
                        snapshot.BaseCaregiverFee, snapshot.PlatformFeeAmount, snapshot.TaxAmount,
                        snapshot.PlatformChargeRuleVersion!.Value, request.UtcNow, _timing.CheckoutHoldDuration);
                }
                catch (InvalidOperationException)
                {
                    return InvalidState();
                }

                db.Bookings.Add(extension);
                await db.SaveChangesAsync(ct);
                return Result<CareHomeBookingResponse>.Success(CheckoutHandler.Map(extension));
            }, ct);
        }
        catch (CareHomeCapacityConflictException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.CapacityConflict", "The selected room type is no longer available for the extension period."));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while the extension was being requested."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));

    private static Result<CareHomeBookingResponse> InvalidState() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "Only the current paid stay can be extended."));
}
