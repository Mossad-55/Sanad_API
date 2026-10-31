using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Discovery;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Families;

namespace Ahlena.API.CaregiversIntegration;

public sealed class CaregiverBookingRatingEligibilityGateway : ICaregiverBookingRatingEligibility
{
    private readonly IFamiliesDbContext _familiesDbContext;

    public CaregiverBookingRatingEligibilityGateway(IFamiliesDbContext familiesDbContext)
    {
        _familiesDbContext = familiesDbContext;
    }

    public async Task<AuthorizedCompletedCaregiverBooking?> GetEligibleBookingAsync(
        Guid bookingId,
        UserId actorUserId,
        CancellationToken cancellationToken)
    {
        List<FamilyId> familyIds = await _familiesDbContext.Families
            .AsNoTracking()
            .Where(item => item.DeletedOnUtc == null &&
                (item.OwnerUserId == actorUserId || item.Members.Any(member => member.Id == actorUserId)))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (familyIds.Count == 0)
            return null;

        Booking? booking = await _familiesDbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == new BookingId(bookingId) &&
                familyIds.Contains(item.FamilyId) &&
                item.Status == BookingStatus.Completed,
                cancellationToken);

        return booking is null
            ? null
            : new AuthorizedCompletedCaregiverBooking(
                booking.FamilyId.Value,
                booking.CaregiverId);
    }
}
