using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

public interface IFinanceDbContext
{
    DbSet<PlatformChargeRule> PlatformChargeRules { get; }
    DbSet<CaregiverPayoutPolicy> CaregiverPayoutPolicies { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPlatformChargeRuleWriter
{
    Task<Result<Guid>> CreateAsync(decimal feeRatePercentage, decimal taxRatePercentage, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken);
}

public sealed record CreatePlatformChargeRuleCommand(decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version, DateTime EffectiveOnUtc) : ICommand<Guid>;
public sealed record PlatformChargeRuleResponse(Guid Id, decimal PlatformFeeRatePercentage, decimal TaxRatePercentage, int Version, DateTime EffectiveOnUtc, DateTime CreatedOnUtc, bool IsActive);
public sealed record GetCurrentPlatformChargeRuleQuery : IQuery<PlatformChargeRuleResponse?>;
public sealed record GetPlatformChargeRuleHistoryQuery : IQuery<IReadOnlyList<PlatformChargeRuleResponse>>;

public sealed class CreatePlatformChargeRuleHandler(IPlatformChargeRuleWriter writer) : ICommandHandler<CreatePlatformChargeRuleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePlatformChargeRuleCommand request, CancellationToken ct)
    {
        try
        {
            return await writer.CreateAsync(request.PlatformFeeRatePercentage, request.TaxRatePercentage, request.Version, request.EffectiveOnUtc, ct);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException ex)
        {
            return Result<Guid>.Failure(new("Finance.Charges.Invalid", ex.Message));
        }
    }
}

public sealed class GetCurrentPlatformChargeRuleHandler(IFinanceDbContext db) : IQueryHandler<GetCurrentPlatformChargeRuleQuery, PlatformChargeRuleResponse?>
{
    public async Task<Result<PlatformChargeRuleResponse?>> Handle(GetCurrentPlatformChargeRuleQuery request, CancellationToken ct)
        => Map(await db.PlatformChargeRules.AsNoTracking().Where(x => x.EffectiveOnUtc <= DateTime.UtcNow).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct));
    private static PlatformChargeRuleResponse? Map(PlatformChargeRule? x) => x is null ? null : new(x.Id, x.PlatformFeeRatePercentage, x.TaxRatePercentage, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, x.IsActive);
}

public sealed class GetPlatformChargeRuleHistoryHandler(IFinanceDbContext db) : IQueryHandler<GetPlatformChargeRuleHistoryQuery, IReadOnlyList<PlatformChargeRuleResponse>>
{
    public async Task<Result<IReadOnlyList<PlatformChargeRuleResponse>>> Handle(GetPlatformChargeRuleHistoryQuery request, CancellationToken ct)
        => (await db.PlatformChargeRules.AsNoTracking().OrderByDescending(x => x.Version).ToListAsync(ct)).Select(x => new PlatformChargeRuleResponse(x.Id, x.PlatformFeeRatePercentage, x.TaxRatePercentage, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, x.IsActive)).ToList();
}
