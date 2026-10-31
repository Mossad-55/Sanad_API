using Ahlena.Modules.Chat.Application.Messages;

namespace Ahlena.Modules.Chat.Application.Sync;

/// <summary>
/// One ordered durable change. `Revision` is a decimal string (no JavaScript
/// integer precision loss). `Type` is `MessageCreated` (payload = the full
/// REST message item) or `MessageReceiptUpdated` (payload = the receipt
/// envelope). Snapshot pages carry `Revision "0"` per message: clients
/// deduplicate snapshot content by message id and changes by revision.
/// </summary>
public sealed record SyncChangeItem(
    string Revision,
    string Type,
    object Payload);

/// <summary>
/// Receipt payload of a `MessageReceiptUpdated` change. Nullable stamps mean
/// absent; clients merge monotonically and tolerate duplicates, out-of-order
/// events, and unknown ids by hydrating through REST.
/// </summary>
public sealed record SyncReceiptPayload(
    Guid ConversationId,
    Guid MessageId,
    DateTime? DeliveredOnUtc,
    DateTime? ReadOnUtc);

/// <summary>
/// `GET /api/v1/chat/conversations/{id}/sync` page. A caught-up page still
/// returns `NextCursor` for the next poll. `ResetRequired: true` (with an
/// empty page and null cursor) means the cursor version is no longer
/// supported: start a new snapshot. Recovery is never truncated to the
/// newest rows.
/// </summary>
public sealed record ConversationSyncPage(
    IReadOnlyList<SyncChangeItem> Items,
    string? NextCursor,
    bool HasMore,
    bool ResetRequired);

/// <summary>
/// Authenticated, conversation-scoped sync cursor. Clients never construct
/// or edit it; the server protects it opaquely. `Mode` is `snapshot` (paged
/// authoritative content up to `Boundary`) or `changes` (deltas above
/// `Revision`).
/// </summary>
public sealed record ChatSyncCursor(
    int Version,
    string Mode,
    Guid ConversationId,
    Guid RequesterId,
    long Revision,
    long Boundary,
    DateTime? LastSentOnUtc,
    Guid? LastMessageId)
{
    public const int CurrentVersion = 1;
    public const string SnapshotMode = "snapshot";
    public const string ChangesMode = "changes";
}

/// <summary>
/// Opaque cursor protection seam (API implements it over DataProtection;
/// tests use an in-memory fake). Validation never reveals another
/// conversation's existence.
/// </summary>
public interface IChatSyncCursorProtector
{
    string Protect(ChatSyncCursor cursor);

    bool TryUnprotect(string? token, out ChatSyncCursor? cursor);
}
