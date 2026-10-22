using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.UnitTests.Support;

/// <summary>
/// A pass-through <see cref="IFinanceDbContext"/> over a real
/// <see cref="Sanad.Modules.Finance.Infrastructure.FinanceDbContext"/> so the
/// Finance application seams can be exercised against an in-memory provider,
/// mirroring <see cref="FamiliesDbContextAdapter"/>.
/// </summary>
internal abstract class FinanceDbContextAdapter(IFinanceDbContext inner)
    : IFinanceDbContext
{
    protected IFinanceDbContext Inner { get; } = inner;

    public DbSet<PlatformChargeRule> PlatformChargeRules => Inner.PlatformChargeRules;
    public DbSet<CaregiverPayoutPolicy> CaregiverPayoutPolicies => Inner.CaregiverPayoutPolicies;
    public DbSet<PlatformControlState> PlatformControlStates => Inner.PlatformControlStates;
    public DbSet<PlatformControlAuditEntry> PlatformControlAuditEntries => Inner.PlatformControlAuditEntries;
    public DbSet<RefundLedger> RefundLedgers => Inner.RefundLedgers;
    public DbSet<RefundLedgerEntry> RefundLedgerEntries => Inner.RefundLedgerEntries;
    public DbSet<CreditBalance> CreditBalances => Inner.CreditBalances;
    public DbSet<ComplaintReview> ComplaintReviews => Inner.ComplaintReviews;
    public DbSet<SubscriptionRefundRequest> SubscriptionRefundRequests => Inner.SubscriptionRefundRequests;
    public DbSet<SubscriptionRefundBudget> SubscriptionRefundBudgets => Inner.SubscriptionRefundBudgets;

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Inner.SaveChangesAsync(cancellationToken);
}
