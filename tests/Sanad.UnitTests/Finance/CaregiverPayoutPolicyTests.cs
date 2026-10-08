using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.True(policy.IsActive);
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
    public void Deactivate_ShouldClearActiveFlag()
    {
        var policy = CaregiverPayoutPolicy.Create(72, 1, Utc(1), Utc(1));

        policy.Deactivate();

        Assert.False(policy.IsActive);
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
    public async Task Handler_deactivates_previous_policy_and_history_is_descending()
    {
        await using var db = CreateContext();
        var writer = new CaregiverPayoutPolicyStore(db);
        var handler = new CreateCaregiverPayoutPolicyHandler(writer);
        await handler.Handle(new(72, 1, Utc(1)), default);
        await handler.Handle(new(48, 2, Utc(2)), default);

        var history = await new GetCaregiverPayoutPolicyHistoryHandler(db).Handle(new(), default);

        Assert.Equal([2, 1], history.Value.Select(x => x.Version));
        Assert.Equal([true, false], history.Value.Select(x => x.IsActive));
        Assert.Equal(2, (await new GetCurrentCaregiverPayoutPolicyHandler(db).Handle(new(), default)).Value!.Version);
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

        var result = await new GetCurrentCaregiverPayoutPolicyHandler(db).Handle(new(), default);

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
}
