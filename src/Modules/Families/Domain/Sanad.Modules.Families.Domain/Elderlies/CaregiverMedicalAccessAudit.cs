using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Domain.Elderlies;

public enum CaregiverMedicalAccessAuditAction
{
    CreateGrant = 0,
    RevokeGrant = 1
}

public enum CaregiverMedicalAccessAuditDecision
{
    Allowed = 0,
    Denied = 1
}

/// <summary>
/// Append-only audit record of a Family decision to grant or revoke medical
/// access for a named caregiver. Records decisions only; never medical data.
/// </summary>
public sealed class CaregiverMedicalAccessAudit : Entity<Guid>
{
    public const int MaximumReasonCodeLength = 100;
    public const int MaximumCorrelationIdLength = 200;

    private CaregiverMedicalAccessAudit() { }

    private CaregiverMedicalAccessAudit(
        Guid id,
        UserId actorUserId,
        ElderlyId dependentId,
        UserId? granteeUserId,
        MedicalAccessGrantId? grantId,
        CaregiverMedicalAccessAuditAction action,
        CaregiverMedicalAccessAuditDecision decision,
        string? reasonCode,
        MedicalAccessGrantType? grantType,
        bool? canViewRecords,
        bool? canEditRecords,
        bool? canShareWithOthers,
        DateTime? expiresOnUtc,
        DateTime occurredOnUtc,
        string correlationId) : base(id)
    {
        ActorUserId = actorUserId;
        DependentId = dependentId;
        GranteeUserId = granteeUserId;
        GrantId = grantId;
        Action = action;
        Decision = decision;
        ReasonCode = reasonCode;
        GrantType = grantType;
        CanViewRecords = canViewRecords;
        CanEditRecords = canEditRecords;
        CanShareWithOthers = canShareWithOthers;
        ExpiresOnUtc = expiresOnUtc;
        OccurredOnUtc = occurredOnUtc;
        CorrelationId = correlationId;
    }

    public UserId ActorUserId { get; private set; }
    public ElderlyId DependentId { get; private set; }
    public UserId? GranteeUserId { get; private set; }
    public MedicalAccessGrantId? GrantId { get; private set; }
    public CaregiverMedicalAccessAuditAction Action { get; private set; }
    public CaregiverMedicalAccessAuditDecision Decision { get; private set; }
    public string? ReasonCode { get; private set; }
    public MedicalAccessGrantType? GrantType { get; private set; }
    public bool? CanViewRecords { get; private set; }
    public bool? CanEditRecords { get; private set; }
    public bool? CanShareWithOthers { get; private set; }
    public DateTime? ExpiresOnUtc { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }
    public string CorrelationId { get; private set; } = default!;

    public static CaregiverMedicalAccessAudit Create(
        UserId actorUserId,
        ElderlyId dependentId,
        UserId? granteeUserId,
        MedicalAccessGrantId? grantId,
        CaregiverMedicalAccessAuditAction action,
        CaregiverMedicalAccessAuditDecision decision,
        string? reasonCode,
        MedicalAccessGrantType? grantType,
        bool? canViewRecords,
        bool? canEditRecords,
        bool? canShareWithOthers,
        DateTime? expiresOnUtc,
        DateTime occurredOnUtc,
        string correlationId)
    {
        if (actorUserId == UserId.Empty) throw new DomainException("Audit actor is required.");
        if (dependentId == ElderlyId.Empty) throw new DomainException("Audit dependent is required.");
        if (!Enum.IsDefined(action)) throw new DomainException("Audit action is invalid.");
        if (!Enum.IsDefined(decision)) throw new DomainException("Audit decision is invalid.");
        if (grantType.HasValue && !Enum.IsDefined(grantType.Value)) throw new DomainException("Audit grant type is invalid.");
        if (occurredOnUtc.Kind != DateTimeKind.Utc) throw new DomainException("Audit occurred time must be UTC.");
        if (expiresOnUtc.HasValue && expiresOnUtc.Value.Kind != DateTimeKind.Utc)
            throw new DomainException("Audit expiry time must be UTC.");
        if (decision == CaregiverMedicalAccessAuditDecision.Denied && string.IsNullOrWhiteSpace(reasonCode))
            throw new DomainException("Audit reason code is required for denied decisions.");
        if (decision == CaregiverMedicalAccessAuditDecision.Denied &&
            (granteeUserId.HasValue || grantId.HasValue || grantType.HasValue || canViewRecords.HasValue ||
             canEditRecords.HasValue || canShareWithOthers.HasValue || expiresOnUtc.HasValue))
            throw new DomainException("Denied audit details must not contain attempted grant data.");
        string? normalizedReason = null;
        if (reasonCode is not null)
        {
            if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Trim().Length > MaximumReasonCodeLength)
                throw new DomainException("Audit reason code is invalid.");
            normalizedReason = reasonCode.Trim();
        }

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > MaximumCorrelationIdLength)
            throw new DomainException("Audit correlation ID is invalid.");

        return new CaregiverMedicalAccessAudit(
            Guid.NewGuid(),
            actorUserId,
            dependentId,
            granteeUserId,
            grantId,
            action,
            decision,
            normalizedReason,
            grantType,
            canViewRecords,
            canEditRecords,
            canShareWithOthers,
            expiresOnUtc,
            occurredOnUtc,
            correlationId.Trim());
    }
}
