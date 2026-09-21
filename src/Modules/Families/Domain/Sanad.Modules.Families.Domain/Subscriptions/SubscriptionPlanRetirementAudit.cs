using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class SubscriptionPlanRetirementAudit : Entity<Guid>
{
    private SubscriptionPlanRetirementAudit() { }

    private SubscriptionPlanRetirementAudit(
        Guid id, Guid planVersionId, string planKey, int planVersion,
        UserId actorUserId, string actorRole, bool oldAvailability,
        bool newAvailability, DateTime retiredOnUtc) : base(id)
    {
        PlanVersionId = planVersionId;
        PlanKey = planKey;
        PlanVersion = planVersion;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        OldAvailability = oldAvailability;
        NewAvailability = newAvailability;
        RetiredOnUtc = retiredOnUtc;
    }

    public Guid PlanVersionId { get; private set; }
    public string PlanKey { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string ActorRole { get; private set; } = string.Empty;
    public bool OldAvailability { get; private set; }
    public bool NewAvailability { get; private set; }
    public DateTime RetiredOnUtc { get; private set; }

    public static SubscriptionPlanRetirementAudit Create(
        SubscriptionPlanVersion plan, UserId actorUserId, DateTime retiredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (actorUserId == UserId.Empty)
            throw new ArgumentException("An audit actor is required.", nameof(actorUserId));
        if (retiredOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retirement timestamp must be UTC.", nameof(retiredOnUtc));

        return new SubscriptionPlanRetirementAudit(
            Guid.CreateVersion7(), plan.Id, plan.Key, plan.Version,
            actorUserId, "SuperAdmin", plan.IsAvailableForNewSales, false,
            retiredOnUtc);
    }
}
