using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.CareRecords;

/// <summary>
/// Append-only, payload-free care-record audit. It records WHO did WHAT to
/// WHICH record and WHEN — never the visit content, so an audit trail can be
/// retained forever without duplicating sensitive medical data. Admin reads
/// (locked D4) emit <see cref="CareRecordAuditAction.AdminListed"/> /
/// <see cref="CareRecordAuditAction.AdminDetailRead"/> rows; authoring writes
/// emit Created/Updated/Deleted rows, which is what preserves deletion
/// attribution after the record row itself is removed.
/// <para>
/// <see cref="Source"/> is nullable only for the Admin-list action, where a
/// single read may span several sources; every authoring action names the
/// record's exact source so a Deleted row still states it.
/// </para>
/// </summary>
public sealed class CareRecordAudit : Entity<Guid>
{
    public const int MaximumCorrelationIdLength = 200;

    private CareRecordAudit()
    {
    }

    private CareRecordAudit(
        Guid id,
        Guid? recordId,
        ElderlyId elderlyId,
        CareRecordSource? source,
        UserId actorUserId,
        CareRecordAuditAction action,
        string correlationId,
        DateTime occurredOnUtc)
        : base(id)
    {
        if (action is not (CareRecordAuditAction.AdminListed or CareRecordAuditAction.AdminDetailRead)
            && recordId is null)
        {
            throw new DomainException("An authoring care-record audit row must name its record.");
        }

        // Only authoring rows and admin detail reads are bound to one
        // dependent; the payload-free AdminListed row may span the whole
        // collection and carries no elderly binding at all.
        if (action != CareRecordAuditAction.AdminListed && elderlyId == ElderlyId.Empty)
            throw new DomainException("The care-record audit dependent is required.");

        if (actorUserId == UserId.Empty)
            throw new DomainException("The care-record audit actor is required.");

        if (!Enum.IsDefined(action))
            throw new DomainException("The care-record audit action is invalid.");

        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-record audit timestamps must be UTC.");

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > MaximumCorrelationIdLength)
            throw new DomainException("The care-record audit correlation id is invalid.");

        Id = id;
        RecordId = recordId;
        ElderlyId = elderlyId;
        Source = source;
        ActorUserId = actorUserId;
        Action = action;
        CorrelationId = correlationId.Trim();
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid? RecordId { get; private set; }

    public ElderlyId ElderlyId { get; private set; }

    public CareRecordSource? Source { get; private set; }

    public UserId ActorUserId { get; private set; }

    public CareRecordAuditAction Action { get; private set; }

    public string CorrelationId { get; private set; } = default!;

    public DateTime OccurredOnUtc { get; private set; }

    public static CareRecordAudit RecordAuthored(
        Guid recordId,
        ElderlyId elderlyId,
        CareRecordSource source,
        UserId actorUserId,
        CareRecordAuditAction action,
        string correlationId,
        DateTime utcNow)
    {
        if (recordId == Guid.Empty)
            throw new DomainException("The care-record audit record is required.");

        return new CareRecordAudit(
            Guid.NewGuid(),
            recordId,
            elderlyId,
            source,
            actorUserId,
            action,
            correlationId,
            utcNow);
    }

    /// <summary>Payload-free admin read audit. The list action carries no single record id.</summary>
    public static CareRecordAudit RecordAdminRead(
        Guid? recordId,
        ElderlyId? elderlyId,
        UserId actorUserId,
        CareRecordAuditAction action,
        string correlationId,
        DateTime utcNow)
    {
        if (action is not (CareRecordAuditAction.AdminListed or CareRecordAuditAction.AdminDetailRead))
            throw new DomainException("An admin care-record audit row must record an admin read action.");

        if (action == CareRecordAuditAction.AdminDetailRead && recordId is null)
            throw new DomainException("An admin detail-read audit row must name its record.");

        return new CareRecordAudit(
            Guid.NewGuid(),
            recordId,
            elderlyId ?? ElderlyId.Empty,
            source: null,
            actorUserId,
            action,
            correlationId,
            utcNow);
    }
}
