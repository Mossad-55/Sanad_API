using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

public sealed class CaregiverPayoutPolicyTests
{
    [Fact]
    public void Create_ShouldPersistImmediatePolicyAsActive()
    {
        var policy = CaregiverPayoutPolicy.Create(72, 1, Utc(1), Utc(1));

        Assert.Equal(72, policy.PayoutDelayHours);
        Assert.Equal(1, policy.Version);
        Assert.Equal(Utc(1), policy.EffectiveOnUtc);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-72)]
    public void Create_ShouldRejectNegativeDelay(int payoutDelayHours)
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            CaregiverPayoutPolicy.Create(payoutDelayHours, 1, Utc(1), Utc(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldRejectNonPositiveVersion(int version)
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            CaregiverPayoutPolicy.Create(72, version, Utc(1), Utc(1)));
    }

    [Fact]
    public void Create_ShouldRejectNonUtcEffectiveTime()
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            CaregiverPayoutPolicy.Create(72, 1, new DateTime(2026, 10, 1), Utc(1)));
    }

    [Fact]
    public async Task Handler_rejects_duplicate_version_and_preserves_active_policy()
    {
        await using var db = CreateContext();
        var writer = new CaregiverPayoutPolicyStore(db);
        var handler = new CreateCaregiverPayoutPolicyHandler(writer);
        var first = await handler.Handle(new(72, 1, Utc(1)), default);
        var duplicate = await handler.Handle(new(48, 1, Utc(2)), default);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Finance.PayoutPolicy.VersionConflict", duplicate.Error.Code);
        Assert.Equal(72, (await db.CaregiverPayoutPolicies.SingleAsync()).PayoutDelayHours);
    }

    [Fact]
    public async Task Handler_newer_version_is_current_and_history_flags_agree()
    {
        await using var db = CreateContext();
        var writer = new CaregiverPayoutPolicyStore(db);
        var handler = new CreateCaregiverPayoutPolicyHandler(writer);
        await handler.Handle(new(72, 1, Utc(1)), default);
        await handler.Handle(new(48, 2, Utc(2)), default);

        var history = await new GetCaregiverPayoutPolicyHistoryHandler(
            new CaregiverPayoutPolicyStore(db), new FixedClock(Utc(3))).Handle(new(), default);
        var current = await new GetCurrentCaregiverPayoutPolicyHandler(db, new FixedClock(Utc(3))).Handle(new(), default);

        Assert.Equal([2, 1], history.Value.Select(x => x.Version));
        Assert.Equal([true, false], history.Value.Select(x => x.IsActive));
        Assert.NotNull(current.Value);
        Assert.Equal(2, current.Value!.Version);
        Assert.True(current.Value.IsActive);
        Assert.Equal(
            history.Value.Single(x => x.IsActive).Id,
            current.Value.Id);
    }

    [Fact]
    public async Task Future_policy_is_scheduled_but_not_effective_before_its_time()
    {
        await using var db = CreateContext();
        var store = new CaregiverPayoutPolicyStore(db);
        await new CreateCaregiverPayoutPolicyHandler(store).Handle(new(72, 1, Utc(1)), default);
        await new CreateCaregiverPayoutPolicyHandler(store).Handle(new(48, 2, FarFuture()), default);

        var before = await store.GetEffectiveAsync(Utc(2), default);
        var history = await new GetCaregiverPayoutPolicyHistoryHandler(store, new FixedClock(Utc(2))).Handle(new(), default);

        Assert.NotNull(before);
        Assert.Equal(1, before!.Version);
        Assert.Equal([false, true], history.Value.Select(x => x.IsActive));
        Assert.Equal(
            history.Value.Single(x => x.IsActive).Version,
            before.Version);
    }

    [Fact]
    public async Task Policy_becomes_current_exactly_at_its_effective_time()
    {
        await using var db = CreateContext();
        var store = new CaregiverPayoutPolicyStore(db);
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(48, 2, Utc(10), Utc(1)));
        await db.SaveChangesAsync();

        Assert.Null(await store.GetEffectiveAsync(Utc(9).AddTicks(-1), default));
        var atBoundary = await store.GetEffectiveAsync(Utc(10), default);
        Assert.NotNull(atBoundary);
        Assert.Equal(2, atBoundary!.Version);
    }

    [Fact]
    public async Task Latest_effective_time_takes_precedence_over_version_after_its_boundary()
    {
        await using var db = CreateContext();
        var store = new CaregiverPayoutPolicyStore(db);
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(72, 1, Utc(5), Utc(1)));
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(48, 2, Utc(3), Utc(1)));
        await db.SaveChangesAsync();

        // v2 is effective earlier despite the later-scheduled v1 existing.
        Assert.Equal(2, (await store.GetEffectiveAsync(Utc(4), default))!.Version);
        // At the later scheduled policy's boundary, it becomes current even though its version is lower.
        Assert.Equal(1, (await store.GetEffectiveAsync(Utc(5), default))!.Version);
        Assert.Equal(1, (await store.GetEffectiveAsync(Utc(6), default))!.Version);

        var historyBefore = await new GetCaregiverPayoutPolicyHistoryHandler(store, new FixedClock(Utc(4))).Handle(new(), default);
        var historyAfter = await new GetCaregiverPayoutPolicyHistoryHandler(store, new FixedClock(Utc(5))).Handle(new(), default);
        var currentAfter = await new GetCurrentCaregiverPayoutPolicyHandler(db, new FixedClock(Utc(5))).Handle(new(), default);
        Assert.Equal(2, historyBefore.Value.Single(x => x.IsActive).Version);
        Assert.Equal(1, historyAfter.Value.Single(x => x.IsActive).Version);
        Assert.Equal(1, currentAfter.Value!.Version);
        Assert.Equal(historyAfter.Value.Single(x => x.IsActive).Id, currentAfter.Value.Id);
    }

    [Fact]
    public async Task Equal_effective_times_resolve_to_highest_version()
    {
        await using var db = CreateContext();
        var store = new CaregiverPayoutPolicyStore(db);
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(72, 1, Utc(5), Utc(1)));
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(48, 2, Utc(5), Utc(1)));
        await db.SaveChangesAsync();

        var history = await new GetCaregiverPayoutPolicyHistoryHandler(store, new FixedClock(Utc(5))).Handle(new(), default);

        Assert.Equal(2, (await store.GetEffectiveAsync(Utc(5), default))!.Version);
        Assert.Equal([true, false], history.Value.Select(x => x.IsActive));
    }

    [Fact]
    public async Task Handler_maps_invalid_input_to_policy_invalid_code()
    {
        await using var db = CreateContext();
        var invalid = await new CreateCaregiverPayoutPolicyHandler(new CaregiverPayoutPolicyStore(db)).Handle(
            new(-5, 3, Utc(3)), default);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("Finance.PayoutPolicy.Invalid", invalid.Error.Code);
    }

    [Fact]
    public async Task Reader_fails_closed_before_effective_time_and_returns_effective_policy()
    {
        await using var db = CreateContext();
        db.CaregiverPayoutPolicies.Add(CaregiverPayoutPolicy.Create(72, 1, Utc(10), Utc(1)));
        await db.SaveChangesAsync();
        var reader = new CaregiverPayoutPolicyStore(db);

        Assert.Null(await reader.GetEffectiveAsync(Utc(9), default));
        var effective = await reader.GetEffectiveAsync(Utc(10), default);
        Assert.Equal((72, 1), (effective!.PayoutDelayHours, effective.Version));
    }

    [Fact]
    public async Task Current_query_returns_null_when_no_policy_is_effective()
    {
        await using var db = CreateContext();

        var result = await new GetCurrentCaregiverPayoutPolicyHandler(db, new FixedClock(Utc(2))).Handle(new(), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Finance_registration_exposes_payout_policy_reader_and_writer()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>("ConnectionStrings:FinanceDatabase", "Host=localhost;Database=test;Username=test;Password=test")]).Build();

        services.AddFinanceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICaregiverPayoutPolicyReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICaregiverPayoutPolicyWriter>());
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime FarFuture() => new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
