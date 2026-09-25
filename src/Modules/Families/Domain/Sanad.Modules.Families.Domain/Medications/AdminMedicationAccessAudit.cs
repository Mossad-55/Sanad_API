using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Medications;

/// <summary>Append-only audit marker for privileged operational medication reads.</summary>
public sealed class AdminMedicationAccessAudit : Entity<Guid>
{
    public const int MaximumAccountTypeLength = 50;
    public const int MaximumActionLength = 100;
    public const int MaximumResourceTypeLength = 100;
    public const int MaximumCorrelationIdLength = 200;

    private AdminMedicationAccessAudit() { }

    private AdminMedicationAccessAudit(
        Guid id,
        UserId actorUserId,
        string actorAccountType,
        string action,
        string resourceType,
        Guid? resourceId,
        DateTime occurredOnUtc,
        string correlationId) : base(id)
    {
        ActorUserId = actorUserId;
        ActorAccountType = actorAccountType;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        OccurredOnUtc = occurredOnUtc;
        CorrelationId = correlationId;
    }

    public UserId ActorUserId { get; private set; }
    public string ActorAccountType { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string ResourceType { get; private set; } = default!;
    public Guid? ResourceId { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }
    public string CorrelationId { get; private set; } = default!;

    public static AdminMedicationAccessAudit Create(
        UserId actorUserId,
        string actorAccountType,
        string action,
        string resourceType,
        Guid? resourceId,
        DateTime occurredOnUtc,
        string correlationId)
    {
        if (actorUserId == UserId.Empty) throw new DomainException("Audit actor is required.");
        return new AdminMedicationAccessAudit(
            Guid.NewGuid(),
            actorUserId,
            Normalize(actorAccountType, MaximumAccountTypeLength, "Actor account type"),
            Normalize(action, MaximumActionLength, "Audit action"),
            Normalize(resourceType, MaximumResourceTypeLength, "Audit resource type"),
            resourceId,
            occurredOnUtc,
            Normalize(correlationId, MaximumCorrelationIdLength, "Correlation ID"));
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new DomainException($"{field} is invalid.");
        return value.Trim();
    }
}
