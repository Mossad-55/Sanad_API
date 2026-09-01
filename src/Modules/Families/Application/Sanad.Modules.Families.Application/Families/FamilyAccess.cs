using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

/// <summary>
/// Resolves the family a user acts within and enforces the role matrix:
/// Owner = everything; Editor = manage dependents + view + invite;
/// Viewer = read-only.
/// v1 resolution rule: the family the user owns (owner_user_id is globally
/// unique) takes precedence; otherwise the earliest family they were
/// invited into.
/// </summary>
internal static class FamilyAccess
{
    public static async Task<Family?> ResolveFamilyAsync(
        IFamiliesDbContext dbContext,
        UserId userId,
        CancellationToken cancellationToken)
    {
        Family? ownedFamily =
            await dbContext.Families
                .SingleOrDefaultAsync(
                    family => family.OwnerUserId == userId,
                    cancellationToken);

        if (ownedFamily is not null)
        {
            return ownedFamily;
        }

        return await dbContext.Families
            .Where(family =>
                family.Members.Any(member => member.Id == userId))
            .OrderBy(family => family.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static bool IsMember(
        Family family,
        UserId userId) =>
        family.GetRole(userId) is not null;

    public static bool CanManage(
        Family family,
        UserId userId) =>
        family.GetRole(userId)
            is FamilyRole.Owner
            or FamilyRole.Editor;

    public static bool IsOwner(
        Family family,
        UserId userId) =>
        family.GetRole(userId) == FamilyRole.Owner;
}