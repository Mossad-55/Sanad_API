using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.Finance;

internal sealed class FixedPlatformChargeRules(decimal fee, decimal tax, int version = 1) : IPlatformChargeRuleReader, IPlatformChargeRuleWriter
{
    public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformChargeRuleRates?>(new(fee, tax, version));

    public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>(
            [new(Guid.NewGuid(), fee, tax, version, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), true)]);

    public Task<Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
}

internal sealed class MissingPlatformChargeRules : IPlatformChargeRuleReader, IPlatformChargeRuleWriter
{
    public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult<PlatformChargeRuleRates?>(null);
    public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    public Task<Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Guid>.Failure(new("Finance.Charges.Missing", "missing")));
}
