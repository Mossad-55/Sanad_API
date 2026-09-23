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
        var taxRule = context.Model.FindEntityType(typeof(SubscriptionTaxRule));

        Assert.NotNull(catalog);
        Assert.NotNull(snapshot);
        Assert.NotNull(taxRule);
        Assert.Contains(catalog!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(SubscriptionPlanVersion.Key), nameof(SubscriptionPlanVersion.Version)]));
        Assert.Contains(snapshot!.GetIndexes(), x => x.IsUnique && x.GetFilter() == "is_current = true");
        Assert.DoesNotContain(catalog.GetIndexes(), x => x.IsUnique && x.GetFilter() == "is_available_for_new_sales = true");
        Assert.Equal(DeleteBehavior.Restrict, snapshot.GetForeignKeys().Single().DeleteBehavior);
        Assert.True(catalog.FindProperty(nameof(SubscriptionPlanVersion.IsAvailableForNewSales))!.IsConcurrencyToken);
        Assert.True(catalog.FindProperty(nameof(SubscriptionPlanVersion.IsPublished))!.IsConcurrencyToken);
        var providerPlan = catalog.FindProperty(nameof(SubscriptionPlanVersion.PaymobSubscriptionPlanId));
        Assert.NotNull(providerPlan);
        Assert.True(providerPlan!.IsNullable);
        Assert.Equal("paymob_subscription_plan_id", providerPlan.GetColumnName());
        Assert.Equal(5, taxRule!.FindProperty(nameof(SubscriptionTaxRule.RatePercentage))!.GetPrecision());
        Assert.Equal(2, taxRule.FindProperty(nameof(SubscriptionTaxRule.RatePercentage))!.GetScale());
        Assert.Contains(taxRule.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual([nameof(SubscriptionTaxRule.Version)]) &&
            x.GetDatabaseName() == "ux_subscription_tax_rules_version");
        Assert.Contains(taxRule.GetIndexes(), x => x.IsUnique &&
            x.GetFilter() == "\"is_active\" = TRUE" &&
            x.GetDatabaseName() == "ux_subscription_tax_rules_active");
    }

    [Fact]
    public void Context_forwards_subscription_sets_through_the_application_interface()
    {
        using FamiliesDbContext context = CreateContext();
        var applicationContext = (Sanad.Modules.Families.Application.Abstractions.Data.IFamiliesDbContext)context;

        Assert.Same(context.SubscriptionPlanVersions, applicationContext.SubscriptionPlanVersions);
        Assert.Same(context.FamilySubscriptions, applicationContext.FamilySubscriptions);
        Assert.Same(context.SubscriptionTaxRules, applicationContext.SubscriptionTaxRules);
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

    [Fact]
    public void Retirement_audit_is_append_only_and_snapshot_preserves_catalog_terms()
    {
        using FamiliesDbContext context = CreateContext();
        var plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            isAvailableForNewSales: true,
            createdOnUtc: DateTime.UtcNow,
            publishedOnUtc: DateTime.UtcNow);
        context.SubscriptionPlanVersions.Add(plan);
        context.SaveChanges();

        var snapshot = FamilySubscription.Create(Sanad.BuildingBlocks.Domain.Primitives.Ids.FamilyId.New(), plan);
        context.FamilySubscriptions.Add(snapshot);
        var audit = SubscriptionPlanRetirementAudit.Create(
            plan,
            Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId.New(),
            DateTime.UtcNow);
        context.SubscriptionPlanRetirementAudits.Add(audit);
        plan.RetireFromNewSales();
        context.SaveChanges();

        Assert.Equal(299m, snapshot.Price);
        Assert.Equal("premium", snapshot.PlanKey);
        context.Entry(audit).Property(x => x.ActorRole).CurrentValue = "ContentAdmin";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Tax_rule_save_guard_allows_deactivation_but_rejects_versioned_field_changes_and_deletion()
    {
        using FamiliesDbContext context = CreateContext();
        var rule = SubscriptionTaxRule.Create(15m, 1, DateTime.UtcNow, DateTime.UtcNow);
        context.SubscriptionTaxRules.Add(rule);
        context.SaveChanges();

        context.Entry(rule).Property(x => x.RatePercentage).CurrentValue = 16m;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        rule = context.SubscriptionTaxRules.Single();
        context.Entry(rule).Property(x => x.Version).CurrentValue = 2;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        rule = context.SubscriptionTaxRules.Single();
        context.Entry(rule).Property(x => x.EffectiveOnUtc).CurrentValue = DateTime.UtcNow.AddDays(1);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        rule = context.SubscriptionTaxRules.Single();
        context.Entry(rule).Property(x => x.CreatedOnUtc).CurrentValue = DateTime.UtcNow.AddDays(1);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        rule = context.SubscriptionTaxRules.Single();
        rule.Deactivate();
        context.SaveChanges();
        Assert.False(context.SubscriptionTaxRules.Single().IsActive);

        context.ChangeTracker.Clear();
        context.Remove(context.SubscriptionTaxRules.Single());
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
