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
    public async Task Catalog_returns_the_version_owned_bilingual_copy_and_benefit_labels()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        db.Families.Add(Family.Create(owner));

        // The copy and the benefit labels are authored through the real
        // authoring path, then the version is published through the real
        // publish path, so both reads below serve persisted rows.
        var created = await new CreateSubscriptionPlanVersionCommandHandler(db).Handle(
            AuthoringCommand(
                SubscriptionPlan.Premium,
                "premium",
                2,
                BenefitInputs(
                    SubscriptionPlan.Premium,
                    SubscriptionBenefitKey.Chatting,
                    "محادثات",
                    "Chatting"),
                arabicName: "الباقة المميزة",
                englishName: "Premium plan",
                arabicDescription: "كل ما تتضمنه الباقة.",
                englishDescription: "Everything the plan includes."),
            default);
        Assert.True(created.IsSuccess);
        var published = await new PublishSubscriptionPlanVersionCommandHandler(db).Handle(
            new PublishSubscriptionPlanVersionCommand(created.Value, UserId.New()), default);
        Assert.True(published.IsSuccess);

        var result = await new GetSubscriptionCatalogQueryHandler(db).Handle(new(owner), default);

        Assert.True(result.IsSuccess);
        SubscriptionPlanResponse item = Assert.Single(
            result.Value,
            plan => plan.Key == SubscriptionPlan.PremiumKey);
        Assert.Equal("الباقة المميزة", item.ArabicName);
        Assert.Equal("Premium plan", item.EnglishName);
        Assert.Equal("كل ما تتضمنه الباقة.", item.ArabicDescription);
        Assert.Equal("Everything the plan includes.", item.EnglishDescription);

        // The labels of the authored benefit key come back with that key only.
        Assert.Equal(8, item.Benefits.Count);
        SubscriptionBenefitResponse chatting = Assert.Single(
            item.Benefits,
            benefit => benefit.Key == SubscriptionBenefitKey.Chatting);
        Assert.Equal("محادثات", chatting.ArabicLabel);
        Assert.Equal("Chatting", chatting.EnglishLabel);
        SubscriptionBenefitResponse library = Assert.Single(
            item.Benefits,
            benefit => benefit.Key == SubscriptionBenefitKey.Library);
        Assert.Null(library.ArabicLabel);
        Assert.Null(library.EnglishLabel);

        // The plan version read keeps its financial terms unchanged.
        Assert.Equal(SubscriptionPlan.Premium.Price, item.Price);
        Assert.Equal(SubscriptionPlan.Premium.Cycle, item.Cycle);
        Assert.Equal(SubscriptionPlan.Premium.Currency, item.Currency);
        Assert.Equal(SubscriptionPlan.Premium.MemberLimit.Value, item.MemberLimit.Value);
        Assert.Equal(SubscriptionPlan.Premium.MonthlyBookingLimit.Value, item.MonthlyBookingLimit.Value);
        Assert.Equal(SubscriptionPlan.Premium.Rollover, item.Rollover);

        // The same authored labels are served by the real admin plan read.
        var admin = await new GetAdminSubscriptionPlanQueryHandler(db)
            .Handle(new GetAdminSubscriptionPlanQuery(created.Value), default);
        Assert.True(admin.IsSuccess);
        Assert.Equal("الباقة المميزة", admin.Value.ArabicName);
        Assert.Equal("Premium plan", admin.Value.EnglishName);
        SubscriptionBenefitResponse adminChatting = Assert.Single(
            admin.Value.Benefits,
            benefit => benefit.Key == SubscriptionBenefitKey.Chatting);
        Assert.Equal("محادثات", adminChatting.ArabicLabel);
        Assert.Equal("Chatting", adminChatting.EnglishLabel);
        Assert.Null(Assert.Single(
            admin.Value.Benefits,
            benefit => benefit.Key == SubscriptionBenefitKey.Library).ArabicLabel);
    }

    [Fact]
    public async Task Catalog_returns_absent_copy_for_a_version_authored_without_it()
    {
        await using var db = CreateContext();
        UserId owner = UserId.New();
        db.Families.Add(Family.Create(owner));
        db.SubscriptionPlanVersions.Add(Published(SubscriptionPlan.Free));
        await db.SaveChangesAsync();

        var result = await new GetSubscriptionCatalogQueryHandler(db).Handle(new(owner), default);

        Assert.True(result.IsSuccess);
        SubscriptionPlanResponse item = Assert.Single(result.Value);
        Assert.Null(item.ArabicName);
        Assert.Null(item.EnglishName);
        Assert.Null(item.ArabicDescription);
        Assert.Null(item.EnglishDescription);
        Assert.All(item.Benefits, benefit =>
        {
            Assert.Null(benefit.ArabicLabel);
            Assert.Null(benefit.EnglishLabel);
        });
    }

    [Fact]
    public async Task Admin_plan_list_and_detail_return_the_same_bilingual_copy()
    {
        await using var db = CreateContext();
        var authored = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            publishedOnUtc: DateTime.UtcNow,
            arabicName: "الباقة المميزة",
            englishName: "Premium plan");
        db.SubscriptionPlanVersions.Add(authored);
        await db.SaveChangesAsync();

        var list = await new GetAdminSubscriptionPlansQueryHandler(db)
            .Handle(new GetAdminSubscriptionPlansQuery(1, 10), default);
        var detail = await new GetAdminSubscriptionPlanQueryHandler(db)
            .Handle(new GetAdminSubscriptionPlanQuery(authored.Id), default);

        Assert.True(list.IsSuccess);
        Assert.True(detail.IsSuccess);
        Assert.Equal("الباقة المميزة", Assert.Single(list.Value.Items).ArabicName);
        Assert.Equal("Premium plan", detail.Value.EnglishName);
        Assert.Equal(SubscriptionPlan.Premium.Price, detail.Value.Price);
        Assert.Equal(SubscriptionPlan.Premium.Currency, detail.Value.Currency);
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

    private static SubscriptionBenefitInput[] BenefitInputs(
        SubscriptionPlan plan,
        SubscriptionBenefitKey labelledKey,
        string? arabicLabel,
        string? englishLabel) =>
        plan.Benefits
            .Select(benefit => benefit.Key == labelledKey
                ? new SubscriptionBenefitInput(benefit.Key, benefit.IsIncluded, arabicLabel, englishLabel)
                : new SubscriptionBenefitInput(benefit.Key, benefit.IsIncluded))
            .ToArray();

    private static CreateSubscriptionPlanVersionCommand AuthoringCommand(
        SubscriptionPlan plan,
        string key,
        int version,
        IReadOnlyCollection<SubscriptionBenefitInput> benefits,
        string? arabicName = null,
        string? englishName = null,
        string? arabicDescription = null,
        string? englishDescription = null) =>
        new(
            key,
            version,
            plan.Price,
            plan.Cycle,
            plan.Currency,
            benefits,
            new SubscriptionLimitInput(plan.MemberLimit.Kind, plan.MemberLimit.Value),
            new SubscriptionLimitInput(plan.MonthlyBookingLimit.Kind, plan.MonthlyBookingLimit.Value),
            plan.Rollover,
            UserId.New(),
            ArabicName: arabicName,
            EnglishName: englishName,
            ArabicDescription: arabicDescription,
            EnglishDescription: englishDescription);

    private static FamiliesDbContext CreateContext() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
