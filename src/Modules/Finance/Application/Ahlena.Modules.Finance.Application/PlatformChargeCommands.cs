using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Application;

public interface IFinanceDbContext
{
    DbSet<PlatformChargeRule> PlatformChargeRules { get; }
    DbSet<CaregiverPayoutPolicy> CaregiverPayoutPolicies { get; }
    DbSet<PlatformControlState> PlatformControlStates { get; }
    DbSet<PlatformControlAuditEntry> PlatformControlAuditEntries { get; }
    DbSet<RefundLedger> RefundLedgers { get; }
    DbSet<RefundLedgerEntry> RefundLedgerEntries { get; }
    DbSet<CreditBalance> CreditBalances { get; }

    /// <summary>W3-F3 additive: reviewed complaint cases (clause 5, decisions #11-#14).</summary>
    DbSet<ComplaintReview> ComplaintReviews { get; }

    /// <summary>W3-F3 additive: the M7 idempotency marker for subscription refund requests (design pin item 4).</summary>
    DbSet<SubscriptionRefundRequest> SubscriptionRefundRequests { get; }

    /// <summary>W3-F3 additive: the cumulative refundable budget of one capture — one succeeded payment attempt and the period it paid for (decisions #4 and #6).</summary>
    DbSet<SubscriptionRefundBudget> SubscriptionRefundBudgets { get; }
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
        catch (Ahlena.BuildingBlocks.Domain.Exceptions.DomainException ex)
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
