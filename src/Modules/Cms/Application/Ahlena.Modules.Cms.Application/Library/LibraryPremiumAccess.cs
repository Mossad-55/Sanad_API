using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;

namespace Ahlena.Modules.Cms.Application.Library;

/// <summary>
/// Evaluates the frozen library premium gate:
/// canAccess = !isPremium || active subscription grants PremiumContent (benefit key 8).
/// Family resolution mirrors the Families v1 rule (FamilyAccess): the family the
/// user owns takes precedence, otherwise the earliest family they were invited
/// into; deleted families are excluded.
/// </summary>
internal static class LibraryPremiumAccess
{
    public static async Task<bool> HasPremiumContentAccessAsync(
        IFamiliesDbContext familiesDb,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (userId == UserId.Empty)
            return false;

        Family? family = await familiesDb.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OwnerUserId == userId && item.DeletedOnUtc == null,
                cancellationToken);

        family ??= await familiesDb.Families
            .AsNoTracking()
            .Where(item => item.Members.Any(member => member.Id == userId)
                && item.DeletedOnUtc == null)
            .OrderBy(item => item.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (family is null)
            return false;

        FamilySubscription? subscription = await familiesDb.FamilySubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.FamilyId == family.Id && item.IsCurrent,
                cancellationToken);

        if (subscription is null)
            return false;

        return subscription.Benefits.Any(benefit =>
            benefit.Key == SubscriptionBenefitKey.PremiumContent &&
            benefit.IsIncluded);
    }
}
