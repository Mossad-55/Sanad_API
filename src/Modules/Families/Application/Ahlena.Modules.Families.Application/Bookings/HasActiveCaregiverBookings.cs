using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Bookings;

namespace Ahlena.Modules.Families.Application.Bookings;

/// <summary>
/// SET-8d / D11 guard: does this caregiver still hold a committed slot?
/// PendingPayment is DELIBERATELY excluded — the slot is not committed until
/// the family pays, so an unpaid request must not block account deletion.
/// </summary>
public sealed record HasActiveCaregiverBookingsQuery(
    CaregiverId CaregiverId)
    : IQuery<bool>;

public sealed class HasActiveCaregiverBookingsQueryHandler
    : IQueryHandler<HasActiveCaregiverBookingsQuery, bool>
{
    private readonly IFamiliesDbContext _dbContext;

    public HasActiveCaregiverBookingsQueryHandler(
        IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(
        HasActiveCaregiverBookingsQuery request,
        CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(
                b => b.CaregiverId == request.CaregiverId
                     && (b.Status == BookingStatus.PendingCaregiverApproval
                         || b.Status == BookingStatus.Confirmed
                         || b.Status == BookingStatus.InProgress),
                cancellationToken);

        return exists;
    }
}
