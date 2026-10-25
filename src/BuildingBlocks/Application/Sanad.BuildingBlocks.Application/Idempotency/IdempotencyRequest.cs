namespace Sanad.BuildingBlocks.Application.Idempotency;

/// <summary>
/// The parsed idempotency identity of a create request: the caller-supplied
/// key plus the canonical fingerprint of that request. It travels on the
/// command so the handler can scope the lookup/insert to the authenticated
/// actor, the operation and the key in the same persistence context as the
/// business write.
/// </summary>
public sealed record IdempotencyRequest(
    IdempotencyKey Key,
    string Fingerprint);
