using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Users.Admin;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.API.CareHomesIntegration;

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
