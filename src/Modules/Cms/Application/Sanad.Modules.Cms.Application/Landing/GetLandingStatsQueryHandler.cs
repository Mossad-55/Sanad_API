using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.Modules.Cms.Application.Landing;

/// <summary>
/// Live counters, computed per request (no caching in v1; counts only, no PII).
/// Owner-confirmed predicates: families exclude soft-deleted rows, caregivers
/// count Active rows by type, facilities count Approved rows.
/// </summary>
public sealed class GetLandingStatsQueryHandler :
    IQueryHandler<GetLandingStatsQuery, LandingStatsResponse>
{
    private readonly IFamiliesDbContext _familiesDb;
    private readonly ICaregiversDbContext _caregiversDb;
    private readonly ICareHomesDbContext _careHomesDb;

    public GetLandingStatsQueryHandler(
        IFamiliesDbContext familiesDb,
        ICaregiversDbContext caregiversDb,
        ICareHomesDbContext careHomesDb)
    {
        _familiesDb = familiesDb;
        _caregiversDb = caregiversDb;
        _careHomesDb = careHomesDb;
    }

    public async Task<Result<LandingStatsResponse>> Handle(
        GetLandingStatsQuery request,
        CancellationToken cancellationToken)
    {
        int familiesCount = await _familiesDb.Families
            .AsNoTracking()
            .Where(family => family.DeletedOnUtc == null)
            .CountAsync(cancellationToken);

        int medicalCaregiversCount = await _caregiversDb.Caregivers
            .AsNoTracking()
            .Where(caregiver =>
                caregiver.Status == CaregiverStatus.Active &&
                caregiver.Type == CaregiverType.Medical)
            .CountAsync(cancellationToken);

        int companionCaregiversCount = await _caregiversDb.Caregivers
            .AsNoTracking()
            .Where(caregiver =>
                caregiver.Status == CaregiverStatus.Active &&
                caregiver.Type == CaregiverType.Companion)
            .CountAsync(cancellationToken);

        int approvedFacilitiesCount = await _careHomesDb.Facilities
            .AsNoTracking()
            .Where(facility => facility.Status == CareHomeStatus.Approved)
            .CountAsync(cancellationToken);

        return Result<LandingStatsResponse>.Success(
            new LandingStatsResponse(
                familiesCount,
                medicalCaregiversCount,
                companionCaregiversCount,
                approvedFacilitiesCount));
    }
}
