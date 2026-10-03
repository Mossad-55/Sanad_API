using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Seeding;

/// <summary>
/// Ensures the guarded local fixture has the Finance configuration required by
/// subscription payment-intent scenarios. This is intentionally not a product
/// default and is only invoked by the opt-in Development fixture seeder.
/// </summary>
public sealed class TestPlatformChargeRuleSeeder(
    IFinanceDbContext financeDbContext,
    IPlatformChargeRuleWriter chargeRuleWriter)
{
    public async Task SeedAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        bool hasEffectiveRule = await financeDbContext.PlatformChargeRules
            .AsNoTracking()
            .AnyAsync(rule => rule.EffectiveOnUtc <= utcNow, cancellationToken);

        if (hasEffectiveRule)
        {
            return;
        }

        int latestVersion = await financeDbContext.PlatformChargeRules
            .AsNoTracking()
            .Select(rule => (int?)rule.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var result = await chargeRuleWriter.CreateAsync(
            feeRatePercentage: 1.00m,
            taxRatePercentage: 0.00m,
            requestedVersion: latestVersion + 1,
            effectiveOnUtc: utcNow.AddMinutes(-1),
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            return;
        }

        bool effectiveRuleNowExists = await financeDbContext.PlatformChargeRules
            .AsNoTracking()
            .AnyAsync(rule => rule.EffectiveOnUtc <= utcNow, cancellationToken);

        if (effectiveRuleNowExists)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Test Finance fixture could not provision an effective platform charge rule. "
            + $"Writer error {result.Error.Code}: {result.Error.Message}");
    }
}
