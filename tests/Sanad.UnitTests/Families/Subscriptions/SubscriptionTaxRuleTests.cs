using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionTaxRuleTests
{
    [Fact]
    public void Create_accepts_inclusive_rate_boundaries_and_rounds_to_two_decimals()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zero = SubscriptionTaxRule.Create(0m, 1, created, created);
        var maximum = SubscriptionTaxRule.Create(100m, 2, created, created);
        var rounded = SubscriptionTaxRule.Create(12.346m, 3, created, created);

        Assert.Equal(0m, zero.RatePercentage);
        Assert.Equal(100m, maximum.RatePercentage);
        Assert.Equal(12.35m, rounded.RatePercentage);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Create_rejects_rates_outside_the_inclusive_range(decimal rate)
    {
        var exception = Assert.Throws<DomainException>(() =>
            SubscriptionTaxRule.Create(rate, 1, DateTime.UtcNow, DateTime.UtcNow));

        Assert.Contains("between 0 and 100", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_versions(int version)
    {
        Assert.Throws<DomainException>(() =>
            SubscriptionTaxRule.Create(15m, version, DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void Create_rejects_non_utc_effective_and_creation_timestamps()
    {
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        Assert.Throws<DomainException>(() =>
            SubscriptionTaxRule.Create(15m, 1, unspecified, utc));
        Assert.Throws<DomainException>(() =>
            SubscriptionTaxRule.Create(15m, 1, utc, unspecified));
    }

    [Fact]
    public async Task Create_handler_replaces_the_active_rule_without_changing_prior_version_fields()
    {
        await using var db = CreateContext();
        var firstEffective = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = await new CreateSubscriptionTaxRuleCommandHandler(db).Handle(
            new(14.126m, 1, firstEffective, UserId.New()), default);

        var firstStored = await db.SubscriptionTaxRules.SingleAsync();
        var secondEffective = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = await new CreateSubscriptionTaxRuleCommandHandler(db).Handle(
            new(15m, 2, secondEffective, UserId.New()), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var rules = await db.SubscriptionTaxRules.OrderBy(x => x.Version).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.False(rules[0].IsActive);
        Assert.True(rules[1].IsActive);
        Assert.Equal(firstStored.Id, rules[0].Id);
        Assert.Equal(14.13m, rules[0].RatePercentage);
        Assert.Equal(1, rules[0].Version);
        Assert.Equal(firstEffective, rules[0].EffectiveOnUtc);
        Assert.Equal(firstStored.CreatedOnUtc, rules[0].CreatedOnUtc);
    }

    [Fact]
    public async Task Create_handler_rejects_duplicate_version_without_replacing_the_active_rule()
    {
        await using var db = CreateContext();
        var handler = new CreateSubscriptionTaxRuleCommandHandler(db);
        var first = await handler.Handle(new(10m, 7, DateTime.UtcNow, UserId.New()), default);

        var duplicate = await handler.Handle(new(20m, 7, DateTime.UtcNow.AddDays(1), UserId.New()), default);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Subscriptions.Tax.DuplicateVersion", duplicate.Error.Code);
        var onlyRule = await db.SubscriptionTaxRules.SingleAsync();
        Assert.True(onlyRule.IsActive);
        Assert.Equal(10m, onlyRule.RatePercentage);
    }

    [Fact]
    public async Task Current_query_returns_null_when_no_rule_is_active()
    {
        await using var db = CreateContext();
        var inactive = SubscriptionTaxRule.Create(10m, 1, DateTime.UtcNow, DateTime.UtcNow, isActive: false);
        db.SubscriptionTaxRules.Add(inactive);
        await db.SaveChangesAsync();

        var result = await new GetCurrentSubscriptionTaxRuleQueryHandler(db)
            .Handle(new(), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task History_query_returns_all_rules_in_descending_version_order()
    {
        await using var db = CreateContext();
        var created = DateTime.UtcNow;
        db.SubscriptionTaxRules.AddRange(
            SubscriptionTaxRule.Create(5m, 2, created.AddDays(1), created.AddDays(1), isActive: false),
            SubscriptionTaxRule.Create(0m, 1, created, created, isActive: false),
            SubscriptionTaxRule.Create(20m, 3, created.AddDays(2), created.AddDays(2)));
        await db.SaveChangesAsync();

        var result = await new GetSubscriptionTaxRuleHistoryQueryHandler(db)
            .Handle(new(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([3, 2, 1], result.Value.Select(x => x.Version));
        Assert.Equal([true, false, false], result.Value.Select(x => x.IsActive));
    }

    [Fact]
    public async Task Create_handler_maps_domain_boundaries_to_invalid_error_without_persisting()
    {
        await using var db = CreateContext();
        var handler = new CreateSubscriptionTaxRuleCommandHandler(db);

        var invalidRate = await handler.Handle(new(100.01m, 1, DateTime.UtcNow, UserId.New()), default);
        var invalidVersion = await handler.Handle(new(10m, 0, DateTime.UtcNow, UserId.New()), default);
        var invalidUtc = await handler.Handle(new(10m, 2, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), UserId.New()), default);

        Assert.Equal("Subscriptions.Tax.Invalid", invalidRate.Error.Code);
        Assert.Equal("Subscriptions.Tax.Invalid", invalidVersion.Error.Code);
        Assert.Equal("Subscriptions.Tax.Invalid", invalidUtc.Error.Code);
        Assert.Empty(db.SubscriptionTaxRules);
    }

    [Fact]
    public async Task Create_handler_maps_active_unique_conflict_to_stable_error()
    {
        await using var inner = CreateContext();
        var context = new FailingSaveContext(inner, new DbUpdateException("ux_subscription_tax_rules_active"));

        var result = await new CreateSubscriptionTaxRuleCommandHandler(context).Handle(
            new(10m, 1, DateTime.UtcNow, UserId.New()), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Tax.ActiveConflict", result.Error.Code);
    }

    [Fact]
    public async Task Create_handler_maps_version_unique_conflict_to_stable_error()
    {
        await using var inner = CreateContext();
        var context = new FailingSaveContext(inner, new DbUpdateException("ux_subscription_tax_rules_version"));

        var result = await new CreateSubscriptionTaxRuleCommandHandler(context).Handle(
            new(10m, 1, DateTime.UtcNow, UserId.New()), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Tax.DuplicateVersion", result.Error.Code);
    }

    private static FamiliesDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FailingSaveContext(FamiliesDbContext inner, Exception failure) : IFamiliesDbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Family> Families => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<Elderly> Elderlies => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<FamilyInvitation> Invitations => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<Booking> Bookings => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<BookingCancellationFact> BookingCancellationFacts => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<AssessmentQuestion> AssessmentQuestions => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<AssessmentTier> AssessmentTiers => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<CareAssessment> CareAssessments => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<Medication> Medications => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<MedicationDoseLog> MedicationDoseLogs => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<ElderlyNote> ElderlyNotes => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<ElderlyActivityLog> ElderlyActivityLogs => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<VisitReport> VisitReports => throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SubscriptionTaxRule> SubscriptionTaxRules => inner.SubscriptionTaxRules;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromException<int>(failure);
    }
}
