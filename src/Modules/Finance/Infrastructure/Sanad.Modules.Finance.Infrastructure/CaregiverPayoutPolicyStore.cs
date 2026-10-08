using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.Finance.Infrastructure;

public sealed class CaregiverPayoutPolicyStore(FinanceDbContext dbContext) : ICaregiverPayoutPolicyReader, ICaregiverPayoutPolicyWriter
{
    public async Task<CaregiverPayoutPolicyRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var policy = await dbContext.CaregiverPayoutPolicies.AsNoTracking()
            .Where(x => x.EffectiveOnUtc <= utcNow)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return policy is null ? null : new(policy.PayoutDelayHours, policy.Version);
    }

    public async Task<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken)
        => await dbContext.CaregiverPayoutPolicies.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new CaregiverPayoutPolicyHistoryItem(x.Id, x.PayoutDelayHours, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<Sanad.BuildingBlocks.Application.Results.Result<Guid>> CreateAsync(int payoutDelayHours, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken)
    {
        var latestVersion = await dbContext.CaregiverPayoutPolicies.Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0;
        if (requestedVersion <= latestVersion)
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.PayoutPolicy.VersionConflict", "The payout policy version must be greater than the current Finance version."));
        var active = await dbContext.CaregiverPayoutPolicies.SingleOrDefaultAsync(x => x.IsActive, cancellationToken);
        try
        {
            var policy = Sanad.Modules.Finance.Domain.CaregiverPayoutPolicy.Create(payoutDelayHours, requestedVersion, effectiveOnUtc, isActive: effectiveOnUtc <= DateTime.UtcNow);
            if (policy.IsActive) active?.Deactivate();
            dbContext.CaregiverPayoutPolicies.Add(policy);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Success(policy.Id);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException ex)
        {
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.PayoutPolicy.Invalid", ex.Message));
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains("ux_caregiver_payout_policy_version", StringComparison.Ordinal) || ex.ToString().Contains("ux_caregiver_payout_policy_active", StringComparison.Ordinal))
        {
            return Sanad.BuildingBlocks.Application.Results.Result<Guid>.Failure(new("Finance.PayoutPolicy.Conflict", "The payout policy configuration changed concurrently."));
        }
    }
}
