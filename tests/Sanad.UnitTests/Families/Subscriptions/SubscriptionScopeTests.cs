using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

/// <summary>
/// W3-F3 bounded listing-subscription capability. Clause 9 must apply to a
/// care-home listing subscription as well as a family premium one, and no
/// listing-subscription symbol existed in the source. The scope classifier is
/// the whole capability: it keys off the plan-key namespace so the existing
/// plan-creation surface needs no schema change.
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class SubscriptionScopeTests
{
    [Theory]
    [InlineData("premium")]
    [InlineData("premium-plus")]
    [InlineData("free")]
    [InlineData("family-premium")]
    public void A_non_listing_key_is_a_family_premium_subscription(string key)
    {
        Assert.Equal(SubscriptionScope.FamilyPremium, SubscriptionScopes.ClassifyPlanKey(key));
        Assert.False(SubscriptionScopes.IsCareHomeListing(key));
    }

    [Theory]
    [InlineData("listing-basic")]
    [InlineData("listing-premium")]
    [InlineData("LISTING-BASIC")]
    [InlineData("  listing-basic  ")]
    public void A_listing_namespaced_key_is_a_care_home_listing_subscription(string key)
    {
        Assert.Equal(SubscriptionScope.CareHomeListing, SubscriptionScopes.ClassifyPlanKey(key));
        Assert.True(SubscriptionScopes.IsCareHomeListing(key));
    }

    [Fact]
    public void A_blank_plan_key_is_refused()
    {
        Assert.Throws<DomainException>(() => SubscriptionScopes.ClassifyPlanKey(null));
        Assert.Throws<DomainException>(() => SubscriptionScopes.ClassifyPlanKey("   "));
    }
}
