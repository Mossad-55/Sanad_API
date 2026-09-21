using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record CancelSubscriptionRenewalCommand(UserId UserId) : ICommand;
public sealed record ReenableSubscriptionAutoRenewCommand(UserId UserId) : ICommand;

public sealed class CancelSubscriptionRenewalCommandHandler : ICommandHandler<CancelSubscriptionRenewalCommand>
{
    private static readonly Error SubscriptionNotFound = new("Subscriptions.Subscription.NotFound", "The current subscription was not found.");
    private static readonly Error AlreadyRequested = new("Subscriptions.CancelRenewal.AlreadyRequested", "Subscription renewal cancellation was already requested.");
    private readonly IFamiliesDbContext _db;
    public CancelSubscriptionRenewalCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result> Handle(CancelSubscriptionRenewalCommand request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result.Failure(FamilyErrors.NotOwner);
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, cancellationToken);
        if (subscription is null) return Result.Failure(SubscriptionNotFound);
        if (!subscription.AutoRenewEnabled || subscription.CancellationRequestedOnUtc is not null) return Result.Failure(AlreadyRequested);
        subscription.CancelRenewal(DateTime.UtcNow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRequested);
        }
        return Result.Success();
    }
}

public sealed class ReenableSubscriptionAutoRenewCommandHandler : ICommandHandler<ReenableSubscriptionAutoRenewCommand>
{
    private static readonly Error SubscriptionNotFound = new("Subscriptions.Subscription.NotFound", "The current subscription was not found.");
    private static readonly Error NotCancelled = new("Subscriptions.ReenableAutoRenew.NotCancelled", "Subscription auto-renew is already enabled.");
    private static readonly Error AlreadyRequested = new("Subscriptions.CancelRenewal.AlreadyRequested", "Subscription renewal cancellation was already requested.");
    private readonly IFamiliesDbContext _db;
    public ReenableSubscriptionAutoRenewCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result> Handle(ReenableSubscriptionAutoRenewCommand request, CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId)) return Result.Failure(FamilyErrors.NotOwner);
        var subscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(x => x.FamilyId == family.Id && x.IsCurrent, cancellationToken);
        if (subscription is null) return Result.Failure(SubscriptionNotFound);
        if (subscription.CancellationRequestedOnUtc is null) return Result.Failure(NotCancelled);
        try
        {
            subscription.ReenableAutoRenew(DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception) when (exception.Message == "Subscription current period has ended.")
        {
            return Result.Failure(new Error("Subscriptions.ReenableAutoRenew.PeriodEnded", "The current subscription period has ended."));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRequested);
        }
        return Result.Success();
    }
}

public sealed record RetireSubscriptionPlanCommand(Guid PlanVersionId, UserId ActorUserId) : ICommand;

public sealed class RetireSubscriptionPlanCommandHandler : ICommandHandler<RetireSubscriptionPlanCommand>
{
    private static readonly Error NotFound = new("Subscriptions.Plan.NotFound", "Subscription plan was not found.");
    private static readonly Error NotPublished = new("Subscriptions.Plan.NotPublished", "Only a published subscription plan can be retired.");
    private static readonly Error AlreadyRetired = new("Subscriptions.Plan.AlreadyRetired", "Subscription plan is already retired.");
    private readonly IFamiliesDbContext _dbContext;

    public RetireSubscriptionPlanCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result> Handle(RetireSubscriptionPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlanVersions
            .SingleOrDefaultAsync(item => item.Id == request.PlanVersionId, cancellationToken);

        if (plan is null) return Result.Failure(NotFound);
        if (!plan.IsPublished) return Result.Failure(NotPublished);
        if (!plan.IsAvailableForNewSales) return Result.Failure(AlreadyRetired);

        var retiredOnUtc = DateTime.UtcNow;
        var audit = SubscriptionPlanRetirementAudit.Create(plan, request.ActorUserId, retiredOnUtc);
        plan.RetireFromNewSales();
        _dbContext.SubscriptionPlanRetirementAudits.Add(audit);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(AlreadyRetired);
        }

        return Result.Success();
    }
}
