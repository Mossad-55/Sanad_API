using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Subscriptions;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionQueryTests
{
    [Fact]
    public async Task Catalog_returns_all_published_versions_and_retired_terms_but_not_drafts()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        db.Families.Add(Family.Create(owner));
        db.SubscriptionPlanVersions.AddRange(
            Published(SubscriptionPlan.Free),
            Published(SubscriptionPlan.Premium, available: false),
            SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus));
        await db.SaveChangesAsync();

        var result = await new GetSubscriptionCatalogQueryHandler(db).Handle(new(owner), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, plan => !plan.IsAvailableForNewSales);
        Assert.All(result.Value, plan => Assert.Equal(8, plan.Benefits.Count));
    }

    [Fact]
    public async Task Current_returns_only_current_snapshot_and_null_when_absent()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        var family = Family.Create(owner);
        db.Families.Add(family);
        var old = Published(SubscriptionPlan.Free);
        var current = Published(SubscriptionPlan.Premium);
        db.SubscriptionPlanVersions.AddRange(old, current);
        var oldSnapshot = FamilySubscription.Create(family.Id, old);
        oldSnapshot.MarkNotCurrent();
        var currentSnapshot = FamilySubscription.Create(family.Id, current);
        db.FamilySubscriptions.AddRange(oldSnapshot, currentSnapshot);
        await db.SaveChangesAsync();

        var result = await new GetCurrentSubscriptionQueryHandler(db).Handle(new(owner), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("premium", result.Value.CurrentSubscription!.PlanKey);
        Assert.Equal(currentSnapshot.CreatedOnUtc.AddMonths(1), result.Value.CurrentSubscription.CurrentPeriodEndsOnUtc);
        Assert.True(result.Value.CurrentSubscription.AutoRenewEnabled);

        db.FamilySubscriptions.Single(x => x.IsCurrent).MarkNotCurrent();
        await db.SaveChangesAsync();
        result = await new GetCurrentSubscriptionQueryHandler(db).Handle(new(owner), default);
        Assert.Null(result.Value.CurrentSubscription);
    }

    [Fact]
    public async Task Editor_viewer_non_member_and_deleted_family_are_denied()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();
        var family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));
        family.AddMember(FamilyMember.Create(viewer, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new GetCurrentSubscriptionQueryHandler(db);
        Assert.False((await handler.Handle(new(editor), default)).IsSuccess);
        Assert.False((await handler.Handle(new(viewer), default)).IsSuccess);
        Assert.False((await handler.Handle(new(UserId.New()), default)).IsSuccess);
        family.MarkDeleted("test", null);
        await db.SaveChangesAsync();
        Assert.False((await handler.Handle(new(owner), default)).IsSuccess);
    }

    [Fact]
    public async Task Current_snapshot_keeps_stored_terms_when_catalog_changes_in_memory()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        var family = Family.Create(owner);
        var plan = Published(SubscriptionPlan.Premium);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, plan));
        await db.SaveChangesAsync();

        var result = await new GetCurrentSubscriptionQueryHandler(db).Handle(new(owner), default);
        Assert.Equal(299m, result.Value.CurrentSubscription!.Price);
    }

    [Fact]
    public async Task Admin_plan_list_includes_drafts_published_and_retired_versions()
    {
        await using var db = CreateContext();
        var draft = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        var published = Published(SubscriptionPlan.Premium);
        var retired = Published(SubscriptionPlan.PremiumPlus, available: false);
        db.SubscriptionPlanVersions.AddRange(draft, published, retired);
        await db.SaveChangesAsync();

        var result = await new GetAdminSubscriptionPlansQueryHandler(db)
            .Handle(new GetAdminSubscriptionPlansQuery(1, 100), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.Contains(result.Value.Items, item => !item.IsPublished);
        Assert.Contains(result.Value.Items, item => item.IsPublished && !item.IsAvailableForNewSales);
    }

    [Fact]
    public async Task Admin_subscription_list_includes_family_context_and_current_state()
    {
        await using var db = CreateContext();
        var owner = UserId.New();
        var family = Family.Create(owner, "Admin-visible family");
        var plan = Published(SubscriptionPlan.Premium);
        var subscription = FamilySubscription.Create(family.Id, plan);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var result = await new GetAdminFamilySubscriptionsQueryHandler(db)
            .Handle(new GetAdminFamilySubscriptionsQuery(1, 10), default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(family.Id.Value, item.FamilyId);
        Assert.Equal("Admin-visible family", item.FamilyName);
        Assert.Equal(owner.Value, item.OwnerUserId);
        Assert.True(item.IsCurrent);
        Assert.Equal("premium", item.PlanKey);
    }

    [Fact]
    public async Task Admin_plan_detail_returns_not_found_for_unknown_version()
    {
        await using var db = CreateContext();

        var result = await new GetAdminSubscriptionPlanQueryHandler(db)
            .Handle(new GetAdminSubscriptionPlanQuery(Guid.NewGuid()), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Plan.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Admin_subscription_detail_returns_snapshot_with_family_context()
    {
        await using var db = CreateContext();
        var family = Family.Create(UserId.New(), "Detail family");
        var plan = Published(SubscriptionPlan.Premium);
        var subscription = FamilySubscription.Create(family.Id, plan);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var result = await new GetAdminFamilySubscriptionQueryHandler(db)
            .Handle(new GetAdminFamilySubscriptionQuery(subscription.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(family.Id.Value, result.Value.FamilyId);
        Assert.Equal("Detail family", result.Value.FamilyName);
        Assert.Equal("premium", result.Value.PlanKey);
    }

    private static SubscriptionPlanVersion Published(SubscriptionPlan plan, bool available = true) =>
        SubscriptionPlanVersion.Create(plan, true, available, DateTime.UtcNow, DateTime.UtcNow);

    private static FamiliesDbContext CreateContext() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
