using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Users.Admin;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.CareHomesIntegration;

public sealed class ApiCareHomeOwnerFacilityLookup(ICareHomesDbContext db)
    : ICareHomeOwnerFacilityLookup
{
    public async Task<CareHomeOwnerFacility?> FindAsync(
        UserId ownerUserId,
        CancellationToken cancellationToken)
    {
        return await db.Facilities
            .AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .Select(item => new CareHomeOwnerFacility(
                item.Id.Value,
                item.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
