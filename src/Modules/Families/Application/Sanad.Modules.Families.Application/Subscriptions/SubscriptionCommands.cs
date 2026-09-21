using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

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
