using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit.Sdk;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionTaxPostgresConcurrencyTests
{
    [Fact]
    public async Task Concurrent_creates_leave_one_active_rule_and_map_loser_to_active_conflict()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__FamiliesIntegrationDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__IdentityIntegrationDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw SkipException.ForSkip(
                "Local PostgreSQL integration test skipped. Set a dedicated Families or Identity integration connection string.");

        NpgsqlConnectionStringBuilder connection = new(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Database) ||
            (!connection.Database.Contains("integration", StringComparison.OrdinalIgnoreCase) &&
             !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase)))
        {
            throw SkipException.ForSkip(
                "The PostgreSQL concurrency test requires a database named as an integration or test database.");
        }

        await using FamiliesDbContext setup = CreateContext(connectionString);
        await setup.Database.EnsureDeletedAsync();
        await setup.Database.EnsureCreatedAsync();

        int seedVersion = Random.Shared.Next(1_000_000, 2_000_000);
        setup.SubscriptionTaxRules.Add(
            Sanad.Modules.Families.Domain.Subscriptions.SubscriptionTaxRule.Create(
                10m,
                seedVersion,
                DateTime.UtcNow,
                DateTime.UtcNow));
        await setup.SaveChangesAsync();

        var gate = new SaveChangesGate(2);
        int firstVersion = seedVersion + 1;
        int secondVersion = seedVersion + 2;

        await using FamiliesDbContext firstContext = CreateContext(connectionString, gate);
        await using FamiliesDbContext secondContext = CreateContext(connectionString, gate);

        Task<Results<Guid>> firstTask = RunCreateAsync(firstContext, firstVersion);
        Task<Results<Guid>> secondTask = RunCreateAsync(secondContext, secondVersion);
        Results<Guid>[] results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.IsSuccess);
        Results<Guid> loser = Assert.Single(results, result => !result.IsSuccess);
        Assert.Equal("Subscriptions.Tax.ActiveConflict", loser.ErrorCode);

        await using FamiliesDbContext assertion = CreateContext(connectionString);
        Assert.Equal(1, await assertion.SubscriptionTaxRules.CountAsync(rule => rule.IsActive));
    }

    private static async Task<Results<Guid>> RunCreateAsync(
        FamiliesDbContext context,
        int version)
    {
        var result = await new CreateSubscriptionTaxRuleCommandHandler(context).Handle(
            new(20m, version, DateTime.UtcNow, UserId.New()),
            CancellationToken.None);

        return new(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
    }

    private static FamiliesDbContext CreateContext(
        string connectionString,
        SaveChangesGate? gate = null)
    {
        DbContextOptionsBuilder<FamiliesDbContext> options =
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseNpgsql(connectionString);

        if (gate is not null)
            options.AddInterceptors(gate);

        return new FamiliesDbContext(options.Options);
    }

    private sealed record Results<T>(bool IsSuccess, string? ErrorCode);

    private sealed class SaveChangesGate(int expectedArrivals) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == expectedArrivals)
                _released.TrySetResult(true);

            await _released.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
