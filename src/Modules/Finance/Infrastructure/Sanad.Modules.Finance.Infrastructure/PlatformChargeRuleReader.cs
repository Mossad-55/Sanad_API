using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Finance.Infrastructure;

public sealed class PlatformChargeRuleReader(FinanceDbContext dbContext) : IPlatformChargeRuleReader, IPlatformChargeRuleWriter
{
    public async Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var rule = await dbContext.PlatformChargeRules.AsNoTracking()
            .Where(x => x.EffectiveOnUtc <= utcNow)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return rule is null ? null : new(rule.PlatformFeeRatePercentage, rule.TaxRatePercentage, rule.Version);
    }

    public async Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken)
        => await dbContext.PlatformChargeRules.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new PlatformChargeRuleHistoryItem(x.Id, x.PlatformFeeRatePercentage, x.TaxRatePercentage, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<Sanad.BuildingBlocks.Application.Results.Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken)
    {
        var latestVersion = await dbContext.PlatformChargeRules.Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0;
        if (requestedVersion <= latestVersion)
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.Charges.VersionConflict", "The shared platform charge version must be greater than the current Finance version."));
        var active = await dbContext.PlatformChargeRules.SingleOrDefaultAsync(x => x.IsActive, cancellationToken);
        try
        {
            var rule = Sanad.Modules.Finance.Domain.PlatformChargeRule.Create(feeRatePercentage, taxRatePercentage, requestedVersion, effectiveOnUtc, isActive: effectiveOnUtc <= DateTime.UtcNow);
            if (rule.IsActive) active?.Deactivate();
            dbContext.PlatformChargeRules.Add(rule);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Success(rule.Id);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException ex)
        {
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.Charges.Invalid", ex.Message));
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains("ux_platform_charge_rules_version", StringComparison.Ordinal) || ex.ToString().Contains("ux_platform_charge_rules_active", StringComparison.Ordinal))
        {
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.Charges.Conflict", "The platform charge configuration changed concurrently."));
        }
    }
}
