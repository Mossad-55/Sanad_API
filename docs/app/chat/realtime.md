# Chat realtime (SignalR hub, typing, presence)

Read with the [Chat Plus plan](../../internal/work/chat-plus/plan.md)
(sections 5–6), [additive contract details](../../internal/work/chat-plus/contracts.md),
and [sync.md](sync.md) for recovery. REST stays authoritative; the hub carries
hints only. Examples here are statically authored from the verified behavior —
never executed against a live API or database. No voice.

## Transport and auth

- Hub path `/hubs/chat` with **standard SignalR negotiation** (no custom
  ticket protocol). WebSockets, SSE, and Long Polling where the client SDK
  supports them — fallback is tested per platform, never assumed.
- Authenticate negotiation and transports with the **existing access token**.
  Browser WebSocket/SSE clients may send `?access_token=` **on this hub path
  only**; headers where supported. TLS required. `access_token`, connection
  tokens, `Authorization`, and payloads are redacted in application/proxy/APM
  logs. A SignalR connection token is not an authentication credential.
- Restricted-verification tokens are rejected; Normal access plus a valid JWT
  user id is required. User routing uses the validated JWT `sub`, never a
  client-supplied user id.
- `CloseOnAuthenticationExpiration` has no corresponding server option, so it is
  enforced equivalently: the hub requires the `NormalAccess` policy with
  per-call authorization, access tokens are short-lived (15 minutes), and
  clients refresh and reconnect. Logout/revoke/block/hide/account-disable
  closes or evicts affected connections. Exact origin allowlists are required;
  CORS alone does not protect WebSocket upgrades.

## Methods

| Method | Args | Returns |
|---|---|---|
| `JoinConversation` | `conversationId` | `{ "joined": true, presence }` — current permitted counterpart presence included |
| `LeaveConversation` | `conversationId` | `{ "joined": false }` — also stops this connection's typing state |
| `SetTyping` | `conversationId`, `isTyping` | no content |
| `Heartbeat` | — | no content — refreshes liveness only, never grants permission |

- Join validates current authorization (participant + visible + eligible);
  ineligible join throws a sanitized hub error with
  `Chat.Conversation.NotFound`, never an existence/block reason. Repeats are
  idempotent and return the same state. Rejoin after **every** reconnect.
- `SetTyping` accepts joined eligible participants only; `userId` derives from
  the JWT. Stop/disconnect clears typing state.
- Unjoined, malformed, ineligible, or rate-limited hints are dropped with
  sanitized counters — no free-form error details, message content, or token
  values in errors.

## Events

- `MessageCreated { conversationId, message }` — `message` is the complete
  REST item (File/Location payloads, nullable receipts included).
  `clientMessageId` reconciles optimistic sends; `id` deduplicates.
- `MessageReceiptUpdated { conversationId, messageId, deliveredOnUtc,
  readOnUtc }` — nullable stamps mean absent; merge monotonically.
- `TypingChanged { conversationId, userId, isTyping, expiresOnUtc }` — stop
  sends `isTyping: false`; the server broadcasts stop on **8 s** expiry while
  still eligible.
- `PresenceChanged { userId, isOnline, lastSeenOnUtc }` — a hint, not a
  durable audit.

Only committed state is pushed. No socket event implies physical device
delivery: delivered remains the recipient's REST fetch acknowledgement.
Receipt updates may precede message events or repeat; clients hydrate via REST
and tolerate duplicates, out-of-order events, and unknown IDs.

## Typing and presence privacy

- Owner-only REST settings (see plan section 6):
  `GET /api/v1/chat/privacy` → `200 { showOnlineStatus, shareLastSeen }`;
  `PUT /api/v1/chat/privacy` → `200`, **full replacement**, both booleans
  required (omitted fields are not defaulted). Both default `false` for new
  and legacy users.
- Presence snapshot:
  `GET /api/v1/chat/conversations/{id}/presence` → `200`
  `{ userId, isOnline|null, lastSeenOnUtc|null }`. `null` means
  undisclosed/unknown — never read it as offline. Blocks in either direction
  suppress both fields without removing REST history access.
- Only currently eligible non-hidden conversation participants receive
  presence; Support report inspection grants no presence subscription.
- Online = any eligible live device with heartbeat age < 90 s (OR across
  devices). Last seen is in-memory (last accepted heartbeat, not the timeout
  instant); restart loses it → `null` until new heartbeats. No fabricated last
  seen from login/device timestamps.
- After privacy changes, authorization changes, foreground, and reconnect,
  clients re-apply the REST presence snapshot (events keep boolean `isOnline`
  and cannot grant a new subscription). Disabling privacy withdraws previously
  shown state via the snapshot; clients clear stale dots/last-seen on nulls.

## Budgets

- Max **5 connections/user**, **20** joined conversations per connection.
- Typing: max 1 accepted hint / 2 s / user / conversation. Heartbeat: max 1
  accepted refresh / 30 s / connection. Excess is dropped.
- Hub inbound frame 16 KiB; oversized frames rejected/closed.
- Sliding-window HTTP limits (all `429` + `Retry-After`):

| Resource | Limit |
|---|---|
| Sends | 60/min/user + 30/min/conversation |
| Opens | 30/min/user |
| Uploads | 10/min/user + 2 concurrent/user |
| Reports | 5/hour/user |
| REST sync | 60/min/conversation + 300/min/user |
| Hub negotiate | 30/min/user (+ existing ingress IP controls) |

- Upload quota: 100 MiB live-unbound/user; unbound blobs expire after **24 h**
  (an expired bind reads as `404`). Multipart 52 MiB total / 50 MiB per file.
  Expiry is enforced lazily at bind/quota time — no orphan sweep worker ships
  in this package, so expired blobs await a future deletion worker (see
  [operations.md](operations.md)); bound messages, history, and moderation
  evidence never expire.
- **Single-instance only** until a verified backplane exists. Declared
  multi-instance without configured support fails validation: realtime splits
  while REST persistence stays safe.

## Reconnect and REST-fallback responsibilities

The frontend owns reconnect/polling/reconciliation; the backend supplies the
contracts plus a runnable integration harness:

1. Start connection/event buffering, join authorized conversations.
2. Complete paginated REST sync (see [sync.md](sync.md)); apply durable
   changes in order, dedupe by message id/revision, merge buffered hints, sync
   again.
3. Reconnecting always rejoins and syncs from the last applied cursor.

Privacy settings and presence live in [sync.md](sync.md)-adjacent REST calls
above; the matching Postman sync flow is folder `09.Sync and recovery` and
the privacy/presence flow is folder `10.Privacy and presence` in
[Sanad.App.Chat.postman_collection.json](../../postman/app/Sanad.App.Chat.postman_collection.json).
