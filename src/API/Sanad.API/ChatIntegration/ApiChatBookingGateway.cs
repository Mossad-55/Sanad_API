using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.ChatIntegration;

/// <summary>
/// Open-time booking resolution for endpoint 2's booking-only mode (Opus
/// delta). Tries the Families booking store first, then CareHomes, and
/// answers with the counterpart's user id only when the booking exists, the
/// opener is a party to it, and it is not cancelled/expired/refunded —
/// every other outcome is the same null, which the handler serves as the
/// privacy-preserving 404 so a caller can never probe booking ids.
///
/// Party rules: a Families booking maps family owner/members ↔ the booked
/// caregiver (counterpart from the caregiver side is the booking creator);
/// a CareHome booking maps <c>FamilyUserId</c> ↔ the facility owner.
/// </summary>
public sealed class ApiChatBookingGateway : IChatBookingGateway
{
    private readonly IFamiliesDbContext _families;
    private readonly ICaregiversDbContext _caregivers;
    private readonly ICareHomesDbContext _careHomes;

    public ApiChatBookingGateway(
        IFamiliesDbContext families,
        ICaregiversDbContext caregivers,
        ICareHomesDbContext careHomes)
    {
        _families = families;
        _caregivers = caregivers;
        _careHomes = careHomes;
    }

    public async Task<UserId?> ResolveCounterpartAsync(
        BookingId bookingId,
        UserId openerUserId,
        CancellationToken cancellationToken = default)
    {
        Booking? booking = await _families.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == bookingId,
                cancellationToken);

        if (booking is not null)
        {
            return await ResolveFamilyBookingCounterpartAsync(
                booking,
                openerUserId,
                cancellationToken);
        }

        CareHomeBooking? careHomeBooking = await _careHomes.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == bookingId.Value,
                cancellationToken);

        return careHomeBooking is null
            ? null
            : await ResolveCareHomeBookingCounterpartAsync(
                careHomeBooking,
                openerUserId,
                cancellationToken);
    }

    private async Task<UserId?> ResolveFamilyBookingCounterpartAsync(
        Booking booking,
        UserId openerUserId,
        CancellationToken cancellationToken)
    {
        // The contract's terminal set: cancelled, expired, refunded.
        // (DeclinedByCaregiver is deliberately NOT rejected — the frozen
        // delta lists only cancelled/expired/refunded.)
        if (booking.Status is BookingStatus.CancelledByFamily or
            BookingStatus.CancelledByCaregiver or
            BookingStatus.Refunded or
            BookingStatus.Expired)
        {
            return null;
        }

        var caregiver = await _caregivers.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == booking.CaregiverId,
                cancellationToken);

        if (caregiver is null)
        {
            return null;
        }

        bool openerOnFamily = await _families.Families
            .AsNoTracking()
            .AnyAsync(
                family =>
                    family.Id == booking.FamilyId &&
                    family.DeletedOnUtc == null &&
                    (family.OwnerUserId == openerUserId ||
                     family.Members.Any(member =>
                         member.Id == openerUserId)),
                cancellationToken);

        if (openerOnFamily)
        {
            return caregiver.UserId;
        }

        return openerUserId == caregiver.UserId
            ? booking.CreatedByUserId
            : null;
    }

    private async Task<UserId?> ResolveCareHomeBookingCounterpartAsync(
        CareHomeBooking booking,
        UserId openerUserId,
        CancellationToken cancellationToken)
    {
        // Same terminal set as above, in CareHomes vocabulary.
        if (booking.Status is CareHomeBookingStatus.Expired or
            CareHomeBookingStatus.Cancelled or
            CareHomeBookingStatus.Refunded)
        {
            return null;
        }

        var facility = await _careHomes.Facilities
            .AsNoTracking()
            .Where(item => item.Id == booking.FacilityId)
            .Select(item => new { item.OwnerUserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (facility is null)
        {
            return null;
        }

        if (openerUserId == booking.FamilyUserId)
        {
            return facility.OwnerUserId;
        }

        return openerUserId == facility.OwnerUserId
            ? booking.FamilyUserId
            : null;
    }
}
