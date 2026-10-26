using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

/// <summary>
/// Mobile integration 2026-10-26 (PART A): pins the approved benefit set on both
/// sides of the grant — the plan definition (<see cref="SubscriptionPlan"/>) and
/// the subscription snapshot (<see cref="FamilySubscription.Benefits"/>) — and
/// proves the shipped end state that the frozen library gate
/// (<c>canAccess = !isPremium || PremiumContent granted</c>) computes.
/// <para>
/// Approved set (source of truth): Free grants keys 1,2,3,5; Premium grants
/// 1..7; PremiumPlus grants all 8. <c>Library</c> (key 2) is included on every
/// plan; <c>PremiumContent</c> (key 8) is included on PremiumPlus only. So a
/// Premium snapshot correctly locks premium content, and only a PremiumPlus
/// snapshot unlocks it — no source change is required or made.
/// </para>
/// </summary>
public sealed class SubscriptionPremiumBenefitAccessTests
{
    [Fact]
    public void Subscription_snapshot_copies_the_approved_plan_benefit_set_exactly()
    {
        AssertSnapshot(SubscriptionPlan.Free,
        [
            SubscriptionBenefitKey.Chatting,
            SubscriptionBenefitKey.Library,
            SubscriptionBenefitKey.CommunityForum,
            SubscriptionBenefitKey.BasicSearch
        ]);

        AssertSnapshot(SubscriptionPlan.Premium,
        [
            SubscriptionBenefitKey.Chatting,
            SubscriptionBenefitKey.Library,
            SubscriptionBenefitKey.CommunityForum,
            SubscriptionBenefitKey.FamilyActivityTimeline,
            SubscriptionBenefitKey.BasicSearch,
            SubscriptionBenefitKey.AdvancedSearchFilters,
            SubscriptionBenefitKey.MedicalSummaryExportAndSecureSharing
        ]);

        AssertSnapshot(SubscriptionPlan.PremiumPlus, Enum.GetValues<SubscriptionBenefitKey>());
    }

    [Fact]
    public async Task Premium_snapshot_grants_Library_but_not_PremiumContent_so_premium_library_stays_locked()
    {
        await using FamiliesDbContext families = CreateFamiliesContext();
        await using CmsDbContext cms = CreateCmsContext();

        UserId subscriber = UserId.New();
        Family family = Family.Create(subscriber);
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, version);
        families.Families.Add(family);
        families.SubscriptionPlanVersions.Add(version);
        families.FamilySubscriptions.Add(subscription);
        await families.SaveChangesAsync();

        // The subscription snapshot carries the plan's own grant: the library
        // benefit is present, premium content is deliberately withheld.
        Assert.True(Benefit(subscription, SubscriptionBenefitKey.Library));
        Assert.False(Benefit(subscription, SubscriptionBenefitKey.PremiumContent));

        LibraryContent premium = await CreatePublished(cms, "Video", "Premium video", isPremium: true);

        var detail = await new GetLibraryContentQueryHandler(cms, families).Handle(
            new GetLibraryContentQuery(premium.Id.Value, subscriber.Value), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.True(detail.Value.IsPremium);
        Assert.False(detail.Value.CanAccess);
        Assert.Empty(detail.Value.Sections);
    }

    [Fact]
    public async Task PremiumPlus_snapshot_grants_PremiumContent_so_premium_library_is_accessible()
    {
        await using FamiliesDbContext families = CreateFamiliesContext();
        await using CmsDbContext cms = CreateCmsContext();

        UserId subscriber = UserId.New();
        Family family = Family.Create(subscriber);
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus);
        FamilySubscription subscription = FamilySubscription.Create(family.Id, version);
        families.Families.Add(family);
        families.SubscriptionPlanVersions.Add(version);
        families.FamilySubscriptions.Add(subscription);
        await families.SaveChangesAsync();

        Assert.True(Benefit(subscription, SubscriptionBenefitKey.Library));
        Assert.True(Benefit(subscription, SubscriptionBenefitKey.PremiumContent));

        LibraryContent premium = await CreatePublished(cms, "Video", "Premium video", isPremium: true);

        var detail = await new GetLibraryContentQueryHandler(cms, families).Handle(
            new GetLibraryContentQuery(premium.Id.Value, subscriber.Value), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.True(detail.Value.CanAccess);
        // D6: entitlement alone never exposes the stored playback address.
        Assert.Null(detail.Value.VideoUrl);
    }

    private static void AssertSnapshot(SubscriptionPlan plan, IReadOnlyCollection<SubscriptionBenefitKey> grantedKeys)
    {
        SubscriptionPlanVersion version = SubscriptionPlanVersion.Create(plan);
        FamilySubscription subscription = FamilySubscription.Create(FamilyId.New(), version);

        SubscriptionBenefitKey[] expected = grantedKeys.OrderBy(key => key).ToArray();
        SubscriptionBenefitKey[] actual = subscription.Benefits
            .Where(benefit => benefit.IsIncluded)
            .Select(benefit => benefit.Key)
            .OrderBy(key => key)
            .ToArray();

        // Every benefit is snapshotted exactly once and the granted set is the
        // plan's own set — nothing invented, nothing dropped.
        Assert.Equal(8, subscription.Benefits.Count);
        Assert.Equal(expected, actual);
        Assert.Equal(
            plan.Benefits.Where(benefit => benefit.IsIncluded).Select(benefit => benefit.Key).OrderBy(key => key),
            actual);
    }

    private static bool Benefit(FamilySubscription subscription, SubscriptionBenefitKey key) =>
        subscription.Benefits.Single(benefit => benefit.Key == key).IsIncluded;

    private static async Task<LibraryContent> CreatePublished(
        CmsDbContext db, string type, string title, bool isPremium = false)
    {
        bool isVideo = type == "Video";
        var command = new CreateLibraryContentCommand(
            isVideo ? LibraryContentType.Video : LibraryContentType.Article,
            null,
            title,
            title,
            "ملخص",
            "Summary",
            isVideo ? "https://youtu.be/abc123" : null,
            isVideo ? 300 : null,
            "library/cover.png",
            isPremium,
            isVideo
                ? null
                : [new LibraryContentSectionInput(1, "الأول", "First", ["نقطة"], ["bullet"])]);

        var result = await new CreateLibraryContentCommandHandler(db)
            .Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess);

        LibraryContent content = db.LibraryContents.Single(item => item.Id == new LibraryContentId(result.Value.Id));
        content.Publish();
        await db.SaveChangesAsync();
        return content;
    }

    private static CmsDbContext CreateCmsContext()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Database.EnsureCreated();
        return db;
    }
}
