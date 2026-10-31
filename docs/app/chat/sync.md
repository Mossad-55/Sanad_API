# Chat sync and recovery

Read with the [Chat Plus plan](../../internal/work/chat-plus/plan.md) (section 7)
and [additive contract details](../../internal/work/chat-plus/contracts.md).
REST stays authoritative; sockets are hints. Examples here are statically
authored from the verified behavior — never executed against a live API or
database.

## Endpoint

```text
GET /api/v1/chat/conversations/{id}/sync?cursor=&pageSize=100
```

- Normal JWT, participant + visible only. Unknown, foreign, or hidden
  conversations answer the generic privacy `404` (`Chat.Conversation.NotFound`).
- `pageSize` defaults to `100`, clamped to 1–100 (never rejected).
- `200` body:

```json
{
  "items": [
    {
      "revision": "42",
      "type": "MessageCreated",
      "payload": { "id": "0198e2c2-4444-7a10-8000-00000000b002" }
    },
    {
      "revision": "43",
      "type": "MessageReceiptUpdated",
      "payload": {
        "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
        "messageId": "0198e2c2-4444-7a10-8000-00000000b002",
        "deliveredOnUtc": "2026-10-11T08:00:02Z",
        "readOnUtc": null
      }
    }
  ],
  "nextCursor": "opaque-server-cursor",
  "hasMore": false,
  "resetRequired": false
}
```

- `revision` is a **decimal string** (avoids JavaScript integer precision loss).
- `MessageCreated` payload is the full message item (the endpoint 3 shape in
  [messages.md](messages.md), including File/Location payloads and nullable
  receipts). `MessageReceiptUpdated` payload carries the nullable stamps only;
  clients merge them monotonically.
- A caught-up page still returns `nextCursor` for the next poll. Do not
  advance the stored cursor until its whole page is applied.

## Cursor lifecycle

- No/empty cursor starts a consistent **snapshot**. All snapshot pages share
  one captured revision boundary: pages enumerate immutable messages in stable
  total order, then consume changes beyond that boundary. Concurrent receipt
  changes are recovered by replay, not frozen by the snapshot.
- Cursors are opaque, authenticated/versioned, and scoped to the conversation,
  requester, and snapshot/change mode. Clients never construct or edit them.
  A cursor never crosses users or conversations.
- Malformed, foreign, or requester-mismatched cursor → generic privacy `404`
  (never reveals another conversation's existence).
- Authentic but no-longer-retained or version-unsupported cursor → empty page
  with `resetRequired: true` and `nextCursor: null`. The client discards state
  and starts a new snapshot. Recovery is never truncated to the newest rows.
- `beforeId` stays the older-history pager only; it is not a forward replay
  cursor and no `since` parameter exists.

## Recovery flows

- **Reconnect:** always rejoin (see [realtime.md](realtime.md)) and sync from
  the last applied cursor. Never skip sync because the socket looks healthy.
- **Kill-socket / > 100 missed messages plus receipt-only changes:** re-poll
  sync until `hasMore` is `false`, applying every page in order. Unchanged
  receipts produce no new event, so the replay converges without
  polling-generated loops. Sync receipt reads acknowledge only actual message
  snapshots delivered to the recipient — never a bare revision number or a
  receipt event.
- **Reset:** on `resetRequired: true`, drop deltas and run a full snapshot
  from an empty cursor, then replay changes.

## Polling cadence

- On outage, poll REST sync every **5 s** for open conversations while
  foreground; refresh the conversation list/unread every **15 s**.
- Failures back off with jitter from **1 s to 30 s**. Stop blind retries on
  `401`/`403` and reconcile authorization instead.
- Background pauses foreground polling; OS push is advisory only. Foreground
  sync always runs even if no push arrived. Stale typing/presence clears.

## Dedupe rules

- Deduplicate by message id **and** revision; tolerate duplicates,
  out-of-order events, and unknown ids (hydrate via REST).
- Apply durable sync changes in order first, then merge buffered socket
  events, then sync again.
- Receipt stamps merge monotonically (`null` means absent); never regress a
  stamp from a stale event.

Matching Postman requests:
[Ahlena.App.Chat.postman_collection.json](../../postman/app/Ahlena.App.Chat.postman_collection.json)
(folder `09.Sync and recovery`: snapshot → next page via `{{syncCursor}}` →
receipt delta; test scripts chain `{{syncCursor}}`).
