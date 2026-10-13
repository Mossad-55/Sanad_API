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
/// delta, Chat Plus reopen). Tries the Families booking store first, then
/// CareHomes. Active-only resolution answers with the counterpart's user id
/// only when the booking exists, the opener is a party to it, and it is not
/// cancelled/expired/refunded; the reopen resolution additionally resolves
/// terminal bookings (flagged inactive) so retained history can reopen, and
/// resolves elderly openers through their elderly identity. Every other
/// outcome is the same null, which the handler serves as the
/// privacy-preserving 404 so a caller can never probe booking ids.
///
/// Party rules: a Families booking maps family owner/members ↔ the booked
/// caregiver (counterpart from the caregiver side is the booking creator;
/// the booking's elderly maps to the caregiver); a CareHome booking maps
/// <c>FamilyUserId</c> (or an elderly of its family) ↔ the facility owner.
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
        BookingOpenTarget? target = await ResolveForReopenAsync(
            bookingId,
            openerUserId,
            cancellationToken);

        return target is not null && target.IsActive
            ? target.Counterpart
            : null;
    }

    /// <summary>
    /// Reopen-capable resolution: unknown bookings and bookings the opener
    /// was never a party to are null; terminal bookings still resolve their
    /// parties (with <c>IsActive: false</c>) so retained history can reopen.
    /// Elderly openers resolve explicitly through their elderly identity
    /// (Families booking's elderly row, or membership of the care-home
    /// booking's family).
    /// </summary>
    public async Task<BookingOpenTarget?> ResolveForReopenAsync(
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
            return await ResolveFamilyBookingTargetAsync(
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
            : await ResolveCareHomeBookingTargetAsync(
                careHomeBooking,
                openerUserId,
                cancellationToken);
    }

    private async Task<BookingOpenTarget?> ResolveFamilyBookingTargetAsync(
        Booking booking,
        UserId openerUserId,
        CancellationToken cancellationToken)
    {
        // The contract's terminal set: cancelled, expired, refunded.
        // (DeclinedByCaregiver is deliberately NOT rejected — the frozen
        // delta lists only cancelled/expired/refunded.)
        bool isActive = booking.Status is not (
            BookingStatus.CancelledByFamily or
            BookingStatus.CancelledByCaregiver or
            BookingStatus.Refunded or
            BookingStatus.Expired);

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
            return new BookingOpenTarget(caregiver.UserId, isActive);
        }

        if (openerUserId == caregiver.UserId)
        {
            return new BookingOpenTarget(booking.CreatedByUserId, isActive);
        }

        // Elderly opener: resolves explicitly through the booking's elderly
        // identity — the opener must be this booking's elderly.
        bool openerIsElderly = await _families.Elderlies
            .AsNoTracking()
            .AnyAsync(
                elderly =>
                    elderly.Id == booking.ElderlyId &&
                    elderly.IdentityUserId == openerUserId,
                cancellationToken);

        return openerIsElderly
            ? new BookingOpenTarget(caregiver.UserId, isActive)
            : null;
    }

    private async Task<BookingOpenTarget?> ResolveCareHomeBookingTargetAsync(
        CareHomeBooking booking,
        UserId openerUserId,
        CancellationToken cancellationToken)
    {
        // Same terminal set as above, in CareHomes vocabulary.
        bool isActive = booking.Status is not (
            CareHomeBookingStatus.Expired or
            CareHomeBookingStatus.Cancelled or
            CareHomeBookingStatus.Refunded);

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
            return new BookingOpenTarget(facility.OwnerUserId, isActive);
        }

        if (openerUserId == facility.OwnerUserId)
        {
            return new BookingOpenTarget(booking.FamilyUserId, isActive);
        }

        // Elderly opener: a member of the booking's family through their
        // elderly identity.
        bool openerIsElderly = await _families.Elderlies
            .AsNoTracking()
            .AnyAsync(
                elderly =>
                    elderly.FamilyId == booking.FamilyId &&
                    elderly.IdentityUserId == openerUserId,
                cancellationToken);

        return openerIsElderly
            ? new BookingOpenTarget(facility.OwnerUserId, isActive)
            : null;
    }
}
