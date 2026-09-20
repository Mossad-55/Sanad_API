using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPersistenceModelTests
{
    [Fact]
    public void Model_maps_catalog_snapshot_and_current_pointer_constraints()
    {
        using FamiliesDbContext context = CreateContext();

        var catalog = context.Model.FindEntityType(typeof(SubscriptionPlanVersion));
        var snapshot = context.Model.FindEntityType(typeof(FamilySubscription));

        Assert.NotNull(catalog);
        Assert.NotNull(snapshot);
        Assert.Contains(catalog!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(SubscriptionPlanVersion.Key), nameof(SubscriptionPlanVersion.Version)]));
        Assert.Contains(snapshot!.GetIndexes(), x => x.IsUnique && x.GetFilter() == "is_current = true");
        Assert.DoesNotContain(catalog.GetIndexes(), x => x.IsUnique && x.GetFilter() == "is_available_for_new_sales = true");
        Assert.Equal(DeleteBehavior.Restrict, snapshot.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public void Context_forwards_subscription_sets_through_the_application_interface()
    {
        using FamiliesDbContext context = CreateContext();
        var applicationContext = (Sanad.Modules.Families.Application.Abstractions.Data.IFamiliesDbContext)context;

        Assert.Same(context.SubscriptionPlanVersions, applicationContext.SubscriptionPlanVersions);
        Assert.Same(context.FamilySubscriptions, applicationContext.FamilySubscriptions);
    }

    [Fact]
    public void Save_guard_allows_retirement_but_rejects_other_catalog_and_snapshot_mutations()
    {
        using FamiliesDbContext context = CreateContext();
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        FamilySubscription subscription = FamilySubscription.Create(Sanad.BuildingBlocks.Domain.Primitives.Ids.FamilyId.New(), plan);
        context.AddRange(plan, subscription);
        context.SaveChanges();

        context.Entry(plan).Property(x => x.Price).CurrentValue = 1m;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        plan = context.SubscriptionPlanVersions.Single();
        plan.RetireFromNewSales();
        context.SaveChanges();
        Assert.False(context.SubscriptionPlanVersions.Single().IsAvailableForNewSales);

        context.Entry(plan).Property(x => x.Version).CurrentValue = 2;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        plan = context.SubscriptionPlanVersions.Single();
        context.Remove(plan);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        FamilySubscription tracked = context.FamilySubscriptions.Single();
        tracked.MarkNotCurrent();
        context.SaveChanges();
        Assert.False(context.FamilySubscriptions.Single().IsCurrent);

        tracked = context.FamilySubscriptions.Single();
        context.Entry(tracked).Property(x => x.Price).CurrentValue = 2m;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static FamiliesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FamiliesDbContext(options);
    }
}
