using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.CareHomes.Domain.Idempotency;

/// <summary>
/// One accepted idempotent create attempt in the Care Homes context: the
/// authenticated actor, the operation, the caller-supplied key, the canonical
/// request fingerprint and the byte-exact response snapshot. It mirrors the
/// Families twin; the row lives in this context because a care-home booking
/// checkout writes here. The unique index on
/// <c>(actor_id, operation, idempotency_key)</c> is the exactly-once race
/// boundary, and the row commits in the same <c>SaveChangesAsync</c> as the
/// booking. No foreign key points at the business row.
/// <para>
/// Retention is 7 days by policy, documented only: no expiry logic and no
/// purge worker in this slice. <c>created_on_utc</c> is indexed for a future
/// purge.
/// </para>
/// </summary>
public sealed class IdempotencyRequest
{
    public const int MaximumOperationLength = 64;
    public const int MaximumKeyLength = 128;
    public const int FingerprintLength = 64;

    public const string UniqueIndexName =
        "IX_idempotency_requests_actor_id_operation_idempotency_key";

    public const string CreatedOnIndexName =
        "IX_idempotency_requests_created_on_utc";

    private IdempotencyRequest()
    {
    }

    private IdempotencyRequest(
        Guid id,
        UserId actorId,
        string operation,
        string key,
        string fingerprint,
        Guid? resourceId,
        int responseStatus,
        string responseBody,
        DateTime createdOnUtc)
    {
        Id = id;
        ActorId = actorId;
        Operation = operation;
        Key = key;
        Fingerprint = fingerprint;
        ResourceId = resourceId;
        ResponseStatus = responseStatus;
        ResponseBody = responseBody;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>The authenticated user, never the family or the facility.</summary>
    public UserId ActorId { get; private set; } = UserId.Empty;

    public string Operation { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string Fingerprint { get; private set; } = string.Empty;

    public Guid? ResourceId { get; private set; }

    public int ResponseStatus { get; private set; }

    /// <summary>The exact wire JSON stored when the create first succeeded.</summary>
    public string ResponseBody { get; private set; } = string.Empty;

    public DateTime CreatedOnUtc { get; private set; }

    public static IdempotencyRequest Create(
        UserId actorId,
        string operation,
        string key,
        string fingerprint,
        Guid? resourceId,
        int responseStatus,
        string responseBody,
        DateTime createdOnUtc) =>
        new(
            Guid.NewGuid(),
            actorId,
            operation,
            key,
            fingerprint,
            resourceId,
            responseStatus,
            responseBody,
            createdOnUtc);
}
