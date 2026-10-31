using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Families.Domain.Subscriptions;

/// <summary>
/// Clause 9 applies to <em>both</em> subscription families named by the Owner
/// contract: the family premium plan and the care-home listing subscription.
/// <para>
/// No listing-subscription symbol existed in the source, so this is the bounded
/// capability the W3 brief requires rather than an invented commerce platform:
/// a scope is the classification that decides <em>which</em> cancellable,
/// refundable purchasable an entry describes, never a second money model. It is
/// <em>derived</em> from the plan key (see <see cref="SubscriptionScopes"/>),
/// never persisted: a care-home listing subscription obeys exactly the same
/// cancel-renewal / duplicate-charge / service-shutdown rules as a family
/// premium subscription and records its movements through the same Finance
/// seams.
/// </para>
/// </summary>
public enum SubscriptionScope
{
    /// <summary>A family's premium plan (the existing family subscription).</summary>
    FamilyPremium = 1,

    /// <summary>A care home's listing subscription. Same clause-9 money rules, distinct scope.</summary>
    CareHomeListing = 2
}

/// <summary>
/// Classifies a plan key into its clause-9 <see cref="SubscriptionScope"/>.
/// <para>
/// The scope is derived from the plan key rather than a new hard-coded
/// property so the existing plan-creation surface needs no schema change; a key
/// in the care-home listing namespace (`listing-*`) is a listing subscription,
/// every other key is a family premium subscription. This keeps the bounded
/// capability additive to the subscription subtree and preserves the existing
/// lifecycle byte-for-byte for family plans.
/// </para>
/// </summary>
public static class SubscriptionScopes
{
    /// <summary>The plan-key namespace reserved for care-home listing subscriptions.</summary>
    public const string CareHomeListingKeyPrefix = "listing-";

    public static SubscriptionScope ClassifyPlanKey(string? planKey)
    {
        if (string.IsNullOrWhiteSpace(planKey))
            throw new DomainException("A subscription scope requires a plan key.");

        return planKey.Trim().StartsWith(CareHomeListingKeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? SubscriptionScope.CareHomeListing
            : SubscriptionScope.FamilyPremium;
    }

    public static bool IsCareHomeListing(string? planKey) =>
        ClassifyPlanKey(planKey) == SubscriptionScope.CareHomeListing;
}
