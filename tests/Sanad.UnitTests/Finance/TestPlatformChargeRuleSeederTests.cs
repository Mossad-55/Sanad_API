using Microsoft.EntityFrameworkCore;
using Sanad.API.Seeding;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

public sealed class TestPlatformChargeRuleSeederTests
{
    [Fact]
    public async Task Empty_table_provisions_version_one_effective_active_rule_with_fixture_rates()
    {
        await using var db = CreateContext();
        var before = DateTime.UtcNow;

        await new TestPlatformChargeRuleSeeder(db, new PlatformChargeRuleReader(db))
            .SeedAsync(before);

        var rule = await db.PlatformChargeRules.SingleAsync();
        Assert.Equal(1, rule.Version);
        Assert.Equal(1.00m, rule.PlatformFeeRatePercentage);
        Assert.Equal(0.00m, rule.TaxRatePercentage);
        Assert.True(rule.IsActive);
        Assert.True(rule.EffectiveOnUtc <= before);
    }

    [Fact]
    public async Task Existing_effective_rule_is_preserved_without_a_duplicate()
    {
        await using var db = CreateContext();
        var existing = PlatformChargeRule.Create(7.25m, 3.50m, 4, DateTime.UtcNow.AddMinutes(-5));
        db.PlatformChargeRules.Add(existing);
        await db.SaveChangesAsync();

        await new TestPlatformChargeRuleSeeder(db, new ThrowingWriter())
            .SeedAsync(DateTime.UtcNow);

        var rules = await db.PlatformChargeRules.ToListAsync();
        var rule = Assert.Single(rules);
        Assert.Equal(existing.Id, rule.Id);
        Assert.Equal(4, rule.Version);
        Assert.Equal(7.25m, rule.PlatformFeeRatePercentage);
        Assert.Equal(3.50m, rule.TaxRatePercentage);
    }

    [Fact]
    public async Task Future_only_rule_is_preserved_and_immediate_fixture_rule_uses_next_version()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        db.PlatformChargeRules.Add(
            PlatformChargeRule.Create(9m, 4m, 6, now.AddDays(1), now, isActive: false));
        await db.SaveChangesAsync();

        await new TestPlatformChargeRuleSeeder(db, new PlatformChargeRuleReader(db))
            .SeedAsync(now);

        var rules = await db.PlatformChargeRules.OrderBy(x => x.Version).ToListAsync();
        Assert.Equal([6, 7], rules.Select(x => x.Version));
        Assert.Equal((9m, 4m, false), (rules[0].PlatformFeeRatePercentage, rules[0].TaxRatePercentage, rules[0].IsActive));
        Assert.Equal((1.00m, 0.00m, true), (rules[1].PlatformFeeRatePercentage, rules[1].TaxRatePercentage, rules[1].IsActive));
        Assert.True(rules[1].EffectiveOnUtc <= now);
    }

    [Fact]
    public async Task Repeated_call_is_idempotent()
    {
        await using var db = CreateContext();
        var seeder = new TestPlatformChargeRuleSeeder(db, new PlatformChargeRuleReader(db));

        await seeder.SeedAsync(DateTime.UtcNow);
        var first = await db.PlatformChargeRules.SingleAsync();
        await seeder.SeedAsync(DateTime.UtcNow);

        var rules = await db.PlatformChargeRules.ToListAsync();
        var onlyRule = Assert.Single(rules);
        Assert.Equal(first.Id, onlyRule.Id);
        Assert.Equal(1, onlyRule.Version);
    }

    [Fact]
    public async Task Writer_conflict_is_benign_when_another_process_has_created_an_effective_rule()
    {
        await using var db = CreateContext();
        var competingRule = PlatformChargeRule.Create(2m, 0m, 1, DateTime.UtcNow.AddMinutes(-1));

        await new TestPlatformChargeRuleSeeder(db, new WriterThatPublishesThenConflicts(db, competingRule))
            .SeedAsync(DateTime.UtcNow);

        var rule = Assert.Single(await db.PlatformChargeRules.ToListAsync());
        Assert.Equal(competingRule.Id, rule.Id);
        Assert.Equal(1, rule.Version);
        Assert.Equal(2m, rule.PlatformFeeRatePercentage);
    }

    [Fact]
    public async Task Writer_failure_without_an_effective_rule_throws_clear_exception()
    {
        await using var db = CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TestPlatformChargeRuleSeeder(db, new ThrowingWriter())
                .SeedAsync(DateTime.UtcNow));

        Assert.Contains("could not provision an effective platform charge rule", exception.Message);
        Assert.Contains("Finance.Charges.Conflict", exception.Message);
        Assert.Empty(await db.PlatformChargeRules.ToListAsync());
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class ThrowingWriter : IPlatformChargeRuleWriter
    {
        public Task<Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken)
            => Task.FromResult(Result<Guid>.Failure(new("Finance.Charges.Conflict", "concurrent writer")));
    }

    private sealed class WriterThatPublishesThenConflicts(FinanceDbContext db, PlatformChargeRule rule) : IPlatformChargeRuleWriter
    {
        public async Task<Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken)
        {
            db.PlatformChargeRules.Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Failure(new("Finance.Charges.Conflict", "concurrent writer"));
        }
    }
}
