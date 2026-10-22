using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.MedicalAccess;

/// <summary>
/// Grant-gated caregiver read of a dependent's structured medical profile
/// (mobile item M5). The gate is re-evaluated on every read — never cached —
/// against the SAME persisted grant row:
/// <list type="bullet">
/// <item><description>an ACTIVE grant for this dependent whose
/// <c>granteeUserId</c> is the caller (<c>RevokedOnUtc</c> is null and any
/// <c>ExpiresOnUtc</c> is in the future);</description></item>
/// <item><description>carrying <c>CanViewRecords</c>.</description></item>
/// </list>
/// A missing grant, a revoked or expired grant, a grant to a different
/// grantee, or a grant without view permission is a coded 403
/// (<c>MedicalAccess.RecordAccessDenied</c>). An unknown dependent is
/// <c>MedicalAccess.NotFound</c> (404). The response is the exact
/// <see cref="ElderlyMedicalProfileResponse"/> shape the family read emits —
/// the family-scoped read itself is NOT relaxed.
/// </summary>
public sealed record GetCaregiverDependentMedicalProfileQuery(
    UserId GranteeUserId,
    ElderlyId DependentId) : IQuery<ElderlyMedicalProfileResponse>;

public static class CaregiverMedicalProfileErrors
{
    public static readonly Error NotFound =
        new(
            "MedicalAccess.NotFound",
            "The dependent was not found.");

    public static readonly Error RecordAccessDenied =
        new(
            "MedicalAccess.RecordAccessDenied",
            "The caller has no active medical-access grant that permits viewing this dependent's records.");
}

public sealed class GetCaregiverDependentMedicalProfileQueryHandler(
    IFamiliesDbContext dbContext)
    : IQueryHandler<GetCaregiverDependentMedicalProfileQuery, ElderlyMedicalProfileResponse>
{
    public async Task<Result<ElderlyMedicalProfileResponse>> Handle(
        GetCaregiverDependentMedicalProfileQuery request,
        CancellationToken cancellationToken)
    {
        // The dependent must exist at all before a grant can be meaningful.
        // Its own existence is not itself protected data; the grant is what
        // gates the profile.
        bool dependentExists = await dbContext.Elderlies
            .AsNoTracking()
            .AnyAsync(e => e.Id == request.DependentId, cancellationToken);

        if (!dependentExists)
        {
            return CaregiverMedicalProfileErrors.NotFound;
        }

        DateTime utcNow = DateTime.UtcNow;

        // The gate is rechecked on EVERY read: a revoked or expired grant
        // denies immediately, with no cached decision anywhere.
        bool hasViewGrant = await dbContext.MedicalAccessGrants
            .AsNoTracking()
            .AnyAsync(
                g => g.DependentId == request.DependentId &&
                     g.GranteeUserId == request.GranteeUserId &&
                     g.RevokedOnUtc == null &&
                     (!g.ExpiresOnUtc.HasValue || g.ExpiresOnUtc.Value > utcNow) &&
                     g.CanViewRecords,
                cancellationToken);

        if (!hasViewGrant)
        {
            return CaregiverMedicalProfileErrors.RecordAccessDenied;
        }

        Elderly? elderly = await dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == request.DependentId, cancellationToken);

        if (elderly is null)
        {
            return CaregiverMedicalProfileErrors.NotFound;
        }

        // The SAME projection the family read emits. Reading through a grant
        // does not relax or alter the family-scoped route.
        return ElderlyMedicalProfileMappings.ToResponse(elderly.Id, elderly.MedicalProfile);
    }
}
