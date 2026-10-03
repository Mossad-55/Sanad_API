using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

public sealed class PlatformChargeRuleTests
{
    [Fact]
    public void Calculator_rounds_each_component_and_total_from_the_base_amount()
    {
        var quote = PlatformChargeCalculator.Calculate(100.005m, 12.345m, 5.555m);

        Assert.Equal(100.00m, quote.BaseAmount);
        Assert.Equal(12.34m, quote.PlatformFeeAmount);
        Assert.Equal(5.56m, quote.TaxAmount);
        Assert.Equal(117.90m, quote.TotalAmount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    public void Calculator_accepts_inclusive_rate_boundaries(decimal fee, decimal tax)
    {
        var quote = PlatformChargeCalculator.Calculate(100m, fee, tax);

        Assert.Equal(fee, quote.PlatformFeeRatePercentage);
        Assert.Equal(tax, quote.TaxRatePercentage);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, 101, 1)]
    [InlineData(1, 1, 101)]
    public void Calculator_rejects_invalid_base_or_rates(decimal baseAmount, decimal fee, decimal tax)
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            PlatformChargeCalculator.Calculate(baseAmount, fee, tax));
    }

    [Fact]
    public async Task Handler_rejects_duplicate_version_and_preserves_active_rule()
    {
        await using var db = CreateContext();
        var writer = new PlatformChargeRuleReader(db);
        var handler = new CreatePlatformChargeRuleHandler(writer);
        var first = await handler.Handle(
            new(10m, 5m, 1, Utc(1)), default);
        var duplicate = await handler.Handle(
            new(20m, 6m, 1, Utc(2)), default);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Finance.Charges.VersionConflict", duplicate.Error.Code);
        Assert.Equal(10m, (await db.PlatformChargeRules.SingleAsync()).PlatformFeeRatePercentage);
    }

    [Fact]
    public async Task Handler_deactivates_previous_rule_and_history_is_descending()
    {
        await using var db = CreateContext();
        var writer = new PlatformChargeRuleReader(db);
        var handler = new CreatePlatformChargeRuleHandler(writer);
        await handler.Handle(new(10m, 5m, 1, Utc(1)), default);
        await handler.Handle(new(12m, 7m, 2, Utc(2)), default);

        var history = await new GetPlatformChargeRuleHistoryHandler(db).Handle(new(), default);

        Assert.Equal([2, 1], history.Value.Select(x => x.Version));
        Assert.Equal([true, false], history.Value.Select(x => x.IsActive));
        Assert.Equal(2, (await new GetCurrentPlatformChargeRuleHandler(db).Handle(new(), default)).Value!.Version);
    }

    [Fact]
    public async Task Reader_fails_closed_before_effective_time_and_returns_effective_rule()
    {
        await using var db = CreateContext();
        db.PlatformChargeRules.Add(PlatformChargeRule.Create(10m, 5m, 1, Utc(10), Utc(1)));
        await db.SaveChangesAsync();
        var reader = new PlatformChargeRuleReader(db);

        Assert.Null(await reader.GetEffectiveAsync(Utc(9), default));
        var effective = await reader.GetEffectiveAsync(Utc(10), default);
        Assert.Equal((10m, 5m, 1), (effective!.PlatformFeeRatePercentage, effective.TaxRatePercentage, effective.Version));
    }

    [Fact]
    public async Task Reader_keeps_current_rule_until_scheduled_rule_effective_time()
    {
        await using var db = CreateContext();
        db.PlatformChargeRules.AddRange(
            PlatformChargeRule.Create(10m, 5m, 1, Utc(1), Utc(1)),
            PlatformChargeRule.Create(12m, 7m, 2, Utc(10), Utc(1), isActive: false));
        await db.SaveChangesAsync();
        var reader = new PlatformChargeRuleReader(db);

        Assert.Equal(1, (await reader.GetEffectiveAsync(Utc(9), default))!.Version);
        Assert.Equal(2, (await reader.GetEffectiveAsync(Utc(10), default))!.Version);
    }

    [Fact]
    public async Task Current_query_does_not_select_a_future_rule_or_create_a_gap()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        db.PlatformChargeRules.AddRange(
            PlatformChargeRule.Create(10m, 5m, 1, now.AddMinutes(-5), now.AddMinutes(-5)),
            PlatformChargeRule.Create(12m, 7m, 2, now.AddMinutes(5), now, isActive: false));
        await db.SaveChangesAsync();

        var result = await new GetCurrentPlatformChargeRuleHandler(db).Handle(new(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value!.Version);
    }

    [Fact]
    public async Task Writer_requires_strictly_increasing_versions_and_preserves_future_schedule()
    {
        await using var db = CreateContext();
        var writer = new PlatformChargeRuleReader(db);
        Assert.True((await writer.CreateAsync(10m, 5m, 1, Utc(1), default)).IsSuccess);
        Assert.True((await writer.CreateAsync(12m, 7m, 2, Utc(10), default)).IsSuccess);

        var conflict = await writer.CreateAsync(13m, 8m, 2, Utc(11), default);

        Assert.False(conflict.IsSuccess);
        Assert.Equal("Finance.Charges.VersionConflict", conflict.Error.Code);
        Assert.Equal(2, (await db.PlatformChargeRules.CountAsync()));
        Assert.True((await db.PlatformChargeRules.SingleAsync(x => x.Version == 1)).IsActive);
    }

    [Fact]
    public async Task Public_handler_maps_invalid_input_and_writer_conflict()
    {
        await using var db = CreateContext();
        var invalid = await new CreatePlatformChargeRuleHandler(new PlatformChargeRuleReader(db)).Handle(
            new(101m, 5m, 3, Utc(3)), default);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("Finance.Charges.Invalid", invalid.Error.Code);

        var conflict = await new CreatePlatformChargeRuleHandler(new ConflictWriter()).Handle(
            new(10m, 5m, 4, Utc(4)), default);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("Finance.Charges.Conflict", conflict.Error.Code);
    }

    [Fact]
    public void Finance_registration_exposes_reader_writer_and_defaults_migration_startup_off()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>("ConnectionStrings:FinanceDatabase", "Host=localhost;Database=test;Username=test;Password=test")]).Build();

        services.AddFinanceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlatformChargeRuleReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlatformChargeRuleWriter>());
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FinanceMigrationOptions>>();
        Assert.False(options.Value.ApplyOnStartup);
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class ConflictWriter : IPlatformChargeRuleWriter
    {
        public Task<Sanad.BuildingBlocks.Application.Results.Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.Charges.Conflict", "concurrent")));
    }
}
