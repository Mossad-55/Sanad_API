using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPersistenceIndependentTests
{
    [Fact]
    public void Published_and_unpublished_versions_preserve_their_publication_metadata()
    {
        DateTime createdOnUtc = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime publishedOnUtc = createdOnUtc.AddMinutes(5);

        SubscriptionPlanVersion published = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            isAvailableForNewSales: true,
            createdOnUtc,
            publishedOnUtc);
        SubscriptionPlanVersion draft = SubscriptionPlanVersion.Create(
            SubscriptionPlan.PremiumPlus,
            isPublished: false,
            isAvailableForNewSales: true,
            createdOnUtc);

        Assert.True(published.IsPublished);
        Assert.True(published.IsAvailableForNewSales);
        Assert.Equal(createdOnUtc, published.CreatedOnUtc);
        Assert.Equal(publishedOnUtc, published.PublishedOnUtc);
        Assert.False(draft.IsPublished);
        Assert.Null(draft.PublishedOnUtc);
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            SubscriptionPlanVersion.Create(SubscriptionPlan.Free, isPublished: true));
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            SubscriptionPlanVersion.Create(SubscriptionPlan.Free, publishedOnUtc: publishedOnUtc));
    }

    [Fact]
    public void Snapshot_round_trip_copies_every_plan_term_and_benefit()
    {
        DateTime createdOnUtc = new(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc);
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.PremiumPlus,
            isPublished: true,
            createdOnUtc: createdOnUtc,
            publishedOnUtc: createdOnUtc.AddMinutes(1));
        FamilySubscription snapshot = FamilySubscription.Create(FamilyId.New(), plan, createdOnUtc);
        string databaseName = Guid.NewGuid().ToString();

        using (FamiliesDbContext arrange = CreateContext(databaseName))
        {
            arrange.Add(snapshot);
            arrange.SaveChanges();
        }

        using FamiliesDbContext assertContext = CreateContext(databaseName);
        FamilySubscription persisted = assertContext.FamilySubscriptions
            .Include(x => x.Benefits)
            .Single();

        Assert.Equal(snapshot.FamilyId, persisted.FamilyId);
        Assert.Equal(plan.Key, persisted.PlanKey);
        Assert.Equal(plan.Version, persisted.PlanVersion);
        Assert.Equal(plan.Price, persisted.Price);
        Assert.Equal(plan.Cycle, persisted.Cycle);
        Assert.Equal(plan.Currency, persisted.Currency);
        Assert.Equal(plan.MemberLimitKind, persisted.MemberLimitKind);
        Assert.Equal(plan.MemberLimitValue, persisted.MemberLimitValue);
        Assert.Equal(plan.MonthlyBookingLimitKind, persisted.MonthlyBookingLimitKind);
        Assert.Equal(plan.MonthlyBookingLimitValue, persisted.MonthlyBookingLimitValue);
        Assert.Equal(plan.Rollover, persisted.Rollover);
        Assert.True(persisted.IsCurrent);
        Assert.Equal(plan.Benefits.OrderBy(x => x.Key), persisted.Benefits.OrderBy(x => x.Key));
    }

    [Fact]
    public void Retirement_is_the_only_catalog_change_allowed_after_persistence()
    {
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        string databaseName = Guid.NewGuid().ToString();

        using (FamiliesDbContext arrange = CreateContext(databaseName))
        {
            arrange.Add(plan);
            arrange.SaveChanges();
        }

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            SubscriptionPlanVersion tracked = context.SubscriptionPlanVersions.Single();
            tracked.RetireFromNewSales();
            context.SaveChanges();
        }

        using FamiliesDbContext assertContext = CreateContext(databaseName);
        SubscriptionPlanVersion persisted = assertContext.SubscriptionPlanVersions.Single();
        Assert.False(persisted.IsAvailableForNewSales);
        Assert.Equal(SubscriptionPlan.Free.Price, persisted.Price);
        Assert.Equal(SubscriptionPlan.Free.Version, persisted.Version);
    }

    [Fact]
    public void Catalog_and_snapshot_mutations_other_than_current_pointer_are_rejected()
    {
        AssertCatalogMutationRejected((context, plan) =>
            context.Entry(plan).Property(x => x.Key).CurrentValue = "changed");
        AssertCatalogMutationRejected((context, plan) =>
            context.Entry(plan).Property(x => x.Version).CurrentValue = 2);
        AssertCatalogMutationRejected((context, plan) =>
            context.Entry(plan).Property(x => x.Price).CurrentValue = 1m);
        AssertCatalogMutationRejected((context, plan) =>
            context.Entry(plan).Property(x => x.CreatedOnUtc).CurrentValue = DateTime.UtcNow);
        AssertCatalogMutationRejected((context, plan) =>
            context.Remove(plan));

        AssertSnapshotMutationRejected((context, snapshot) =>
            context.Entry(snapshot).Property(x => x.Price).CurrentValue = 1m);
        AssertSnapshotMutationRejected((context, snapshot) =>
            context.Entry(snapshot).Property(x => x.PlanVersion).CurrentValue = 2);
        AssertSnapshotMutationRejected((context, snapshot) =>
            context.Remove(snapshot));
    }

    [Fact]
    public void Draft_publication_fields_are_the_only_additional_catalog_mutation()
    {
        string databaseName = Guid.NewGuid().ToString();
        var publishedOnUtc = DateTime.UtcNow;
        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
            context.Add(plan);
            context.SaveChanges();
            plan.Publish(publishedOnUtc);
            context.SaveChanges();
        }

        using FamiliesDbContext assertContext = CreateContext(databaseName);
        SubscriptionPlanVersion persisted = assertContext.SubscriptionPlanVersions.Single();
        Assert.True(persisted.IsPublished);
        Assert.Equal(publishedOnUtc, persisted.PublishedOnUtc);
    }

    [Fact]
    public void Snapshot_benefit_rows_are_immutable_but_current_pointer_can_change()
    {
        string databaseName = Guid.NewGuid().ToString();
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        FamilySubscription snapshot = FamilySubscription.Create(FamilyId.New(), plan);

        using (FamiliesDbContext arrange = CreateContext(databaseName))
        {
            arrange.Add(snapshot);
            arrange.SaveChanges();
        }

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            FamilySubscription tracked = context.FamilySubscriptions
                .Include(x => x.Benefits)
                .Single();
            SubscriptionBenefit benefit = tracked.Benefits.First();
            context.Entry(benefit).Property(x => x.IsIncluded).CurrentValue = !benefit.IsIncluded;

            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            FamilySubscription tracked = context.FamilySubscriptions.Single();
            tracked.MarkNotCurrent();
            context.SaveChanges();
        }

        using FamiliesDbContext assertContext = CreateContext(databaseName);
        Assert.False(assertContext.FamilySubscriptions.Single().IsCurrent);
    }

    [Fact]
    public void Model_maps_catalog_snapshot_tables_columns_owned_benefits_indexes_and_restrictive_family_fk()
    {
        using FamiliesDbContext context = CreateContext();
        IEntityType? catalog = context.Model.FindEntityType(typeof(SubscriptionPlanVersion));
        IEntityType? snapshot = context.Model.FindEntityType(typeof(FamilySubscription));
        Assert.NotNull(catalog);
        Assert.NotNull(snapshot);
        IEntityType catalogBenefit = Assert.Single(context.Model.GetEntityTypes(), x =>
            x.ClrType == typeof(SubscriptionBenefit) && x.GetTableName() == "subscription_plan_version_benefits");
        IEntityType snapshotBenefit = Assert.Single(context.Model.GetEntityTypes(), x =>
            x.ClrType == typeof(SubscriptionBenefit) && x.GetTableName() == "family_subscription_benefits");

        Assert.Equal("families", catalog!.GetSchema());
        Assert.Equal("subscription_plan_versions", catalog.GetTableName());
        Assert.Equal("family_subscriptions", snapshot!.GetTableName());
        Assert.Equal("subscription_plan_version_benefits", catalogBenefit.GetTableName());
        Assert.Equal("family_subscription_benefits", snapshotBenefit.GetTableName());

        Assert.Equal("plan_key", catalog.FindProperty(nameof(SubscriptionPlanVersion.Key))!.GetColumnName());
        Assert.Equal("version", catalog.FindProperty(nameof(SubscriptionPlanVersion.Version))!.GetColumnName());
        Assert.Equal("is_published", catalog.FindProperty(nameof(SubscriptionPlanVersion.IsPublished))!.GetColumnName());
        Assert.Equal("is_available_for_new_sales", catalog.FindProperty(nameof(SubscriptionPlanVersion.IsAvailableForNewSales))!.GetColumnName());
        Assert.Equal("published_on_utc", catalog.FindProperty(nameof(SubscriptionPlanVersion.PublishedOnUtc))!.GetColumnName());
        Assert.Equal("family_id", snapshot.FindProperty(nameof(FamilySubscription.FamilyId))!.GetColumnName());
        Assert.Equal("is_current", snapshot.FindProperty(nameof(FamilySubscription.IsCurrent))!.GetColumnName());

        Assert.Contains(catalog.GetIndexes(), x =>
            x.IsUnique && x.GetDatabaseName() == "ux_subscription_plan_versions_key_version" &&
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(SubscriptionPlanVersion.Key), nameof(SubscriptionPlanVersion.Version)]));
        Assert.Contains(snapshot.GetIndexes(), x =>
            x.IsUnique && x.GetDatabaseName() == "ux_family_subscriptions_current" &&
            x.GetFilter() == "is_current = true" &&
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(FamilySubscription.FamilyId)]));
        Assert.DoesNotContain(catalog.GetIndexes(), x => x.IsUnique && x.GetFilter() == "is_available_for_new_sales = true");

        IForeignKey familyForeignKey = Assert.Single(snapshot.GetForeignKeys());
        Assert.Equal(typeof(Family), familyForeignKey.PrincipalEntityType.ClrType);
        Assert.True(familyForeignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, familyForeignKey.DeleteBehavior);
        Assert.Equal("SubscriptionPlanVersionId", catalogBenefit.FindProperty("SubscriptionPlanVersionId")!.Name);
        Assert.Equal("FamilySubscriptionId", snapshotBenefit.FindProperty("FamilySubscriptionId")!.Name);
    }

    private static void AssertCatalogMutationRejected(
        Action<FamiliesDbContext, SubscriptionPlanVersion> mutate)
    {
        using FamiliesDbContext context = CreateContext();
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        context.Add(plan);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        plan = context.SubscriptionPlanVersions.Single();

        mutate(context, plan);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static void AssertSnapshotMutationRejected(
        Action<FamiliesDbContext, FamilySubscription> mutate)
    {
        using FamiliesDbContext context = CreateContext();
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Premium);
        FamilySubscription snapshot = FamilySubscription.Create(FamilyId.New(), plan);
        context.Add(snapshot);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        snapshot = context.FamilySubscriptions.Single();

        mutate(context, snapshot);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static FamiliesDbContext CreateContext(string? databaseName = null)
    {
        DbContextOptions<FamiliesDbContext> options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new FamiliesDbContext(options);
    }
}
