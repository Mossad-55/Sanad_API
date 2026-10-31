# Chat API — messages, unread counts, delivery and read ticks

All Chat app routes require Normal access and live under
`/api/v1/chat`. As on [conversations.md](conversations.md), every route carries
`[Authorize(Policy = NormalAccess)]`, the acting participant always comes from the JWT
`sub` claim, and anonymous callers receive `401`. Sends, opens, uploads, reports,
and sync are additionally bounded by sliding-window rate limits (`429` +
`Retry-After`, see [realtime.md](realtime.md#budgets)).

This page specifies Lane B of the frozen contract in `docs/chat/plan.md`:
endpoints 3 (message cursor list), 4 (send), and 8 (unread totals), plus the
new-message notification side effect. Lane A's surfaces become fully live with
this lane: endpoint 1's `lastMessage`/`unreadCount` stubs are now computed,
read marks (endpoint 7) gain their interpretation, block rows (endpoint 9) are
enforced at send time, and uploaded attachments (endpoint 5) bind to messages
here. Support-side reporting (endpoints 12–15) is specified in
[reports.md](reports.md).

**Privacy posture (applies to every route below).** An unknown id, a foreign
row, a row hidden for the requester, and a denied send are indistinguishable:
they answer `404` with the generic problem body (`title: "Not Found"`,
`detail: "The requested resource was not found."`). The `code` extension names
the failing gate family — `Chat.Conversation.NotFound` (unknown/foreign/hidden
conversation, and the blocked-sender failure), `Chat.Message.NotFound` (an
unresolvable `beforeId`), `Chat.Attachment.NotFound` (an unbindable
attachment) — but never *why*. Failures of the caller's *own* payload (bad
type, empty or oversized text, bad or missing location payload, a reused
`clientMessageId` with different content) are `400`s/`409`s carrying the
specific `Chat.Message.*` code: the sender is entitled to know their own
request was invalid or conflicting.

## List messages — `GET /api/v1/chat/conversations/{conversationId}/messages?beforeId=&pageSize=30`

Returns one cursor page of a conversation's messages, newest first by
(`createdOnUtc`, `id`). The gate runs first: unknown, foreign, and
hidden-for-requester conversations are the one generic privacy `404`
(`Chat.Conversation.NotFound`). Reopen (endpoint 2) restores visibility.

```text
GET /api/v1/chat/conversations/0198e2c2-4444-7a10-8000-00000000c001/messages?pageSize=30
```

- `pageSize` defaults to 30 and is clamped to 1–100 by the handler (values outside
  the range are clamped, never rejected).
- Paging is by opaque cursor, not page number: pass the previous response's
  `nextCursor` back as `beforeId` to fetch the next (older) page. A raw
  message id also works when it resolves inside *this* conversation; anything
  else — malformed string, unknown id, another conversation's message id — is
  the message-scoped privacy `404` (`Chat.Message.NotFound`), so the parameter
  can never be used to probe ids. Omit it for the newest page.
- `nextCursor` is `null` and `hasMore` is `false` on the last page (including
  an empty conversation, which answers `items: []`).

```json
{
  "items": [
    {
      "id": "0198e2c2-4444-7a10-8000-00000000b002",
      "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
      "clientMessageId": "web-0002",
      "senderUserId": "00000000-0000-0000-0000-000000000001",
      "type": "Text",
      "text": "Hello — is Thursday still fine for the visit?",
      "attachment": null,
      "location": null,
      "createdOnUtc": "2026-10-11T09:15:30Z",
      "deliveredOnUtc": "2026-10-11T09:16:02Z",
      "readOnUtc": null
    },
    {
      "id": "0198e2c2-4444-7a10-8000-00000000b001",
      "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
      "clientMessageId": "web-0001",
      "senderUserId": "00000000-0000-0000-0000-000000000002",
      "type": "File",
      "text": null,
      "attachment": { "attachmentId": "0198e2c2-4444-7a10-8000-00000000a001" },
      "location": null,
      "createdOnUtc": "2026-10-11T09:10:00Z",
      "deliveredOnUtc": "2026-10-11T09:16:02Z",
      "readOnUtc": "2026-10-11T09:20:11Z"
    }
  ],
  "nextCursor": "NjM5MjczMDY4NDAwMDAwMDAwOjAxOThlMmMyNDQ0NDdhMTA4MDAwMDAwMDAwMDBiMDAz",
  "hasMore": true
}
```

A `Location` message carries the coordinate payload instead of a body:

```json
{
  "id": "0198e2c2-4444-7a10-8000-00000000b003",
  "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
  "clientMessageId": "web-0003",
  "senderUserId": "00000000-0000-0000-0000-000000000001",
  "type": "Location",
  "text": null,
  "attachment": null,
  "location": { "latitude": 30.0444, "longitude": 31.2357, "label": "Cairo Tower" },
  "createdOnUtc": "2026-10-11T09:18:00Z",
  "deliveredOnUtc": null,
  "readOnUtc": null
}
```

**Item shape.** Every item carries its `conversationId`. `attachment` is the id
reference only — file metadata travels with the file endpoint (endpoint 6), so
the message item never joins the attachment row. `text` is `null` for `File`
and `Location` messages; `location` is `null` for `Text` and `File` messages.
`readOnUtc` is *computed* for the reader of the item (see the read rule
below); it is not a raw column echo.

**Delivery write side effect.** A successful fetch stamps `deliveredOnUtc` —
set-once, server UTC — on every returned message that the requester did not
send and that has no delivery stamp yet, and persists the change. Consequences:

- The sender's own fetch never stamps their own messages.
- Only the messages the fetch actually returns are stamped; older messages
  beyond the current page stay unstamped until a fetch returns them.
- Later fetches no-op on already-delivered messages.

## Send — `POST /api/v1/chat/conversations/{conversationId}/messages`

```json
{
  "clientMessageId": "web-0002",
  "type": "Text",
  "text": "Hello — is Thursday still fine for the visit?",
  "attachmentId": null
}
```

```json
{
  "clientMessageId": "web-0003",
  "type": "Location",
  "location": { "latitude": 30.0444, "longitude": 31.2357, "label": "Cairo Tower" }
}
```

Messages are **immutable once sent** — there is no edit or delete path, and
retention is indefinite. All stamps are server-set UTC; clients cannot supply
timestamps.

| Outcome | Status | Body |
|---|---|---|
| New message created | `201` | the message item (shape above) |
| Exact replay of the same `clientMessageId` | `200` | the original message item (current ticks) |
| Same `clientMessageId`, different content | `409` | `Chat.Message.IdempotencyConflict` problem |

The body is the message item either way; **the status code alone tells create
apart from replay** (mirroring how endpoint 2 reports `created`). Sending is
the only thing that moves `lastActivityOnUtc`, which is what keeps the
conversation list ordering fresh.

**Payload rules (the sender's own `400 Chat.Message.Invalid` triggers).**

- `type` must be the name `Text`, `File`, or `Location` (case-insensitive;
  never a numeric enum value).
- `clientMessageId` is the caller's idempotency key: trimmed, 1–100 characters,
  unique per conversation (database index plus handler pre-check).
- `Text`: `text` required, trimmed, 1–2000 characters; `attachmentId` and the
  location fields must be absent.
- `File`: `attachmentId` required and must not be the empty GUID; `text` and
  the location fields must be absent (a File message never carries a body —
  the domain rejects it too); the attachment must exist **in this
  conversation**, be uploaded by the sender (endpoint 5), and not already
  bound to another message. An unknown/foreign/cross-conversation attachment
  id is instead the privacy `404` (`Chat.Attachment.NotFound`); an
  already-bound one is the `400` (the single-use bind is a caller mistake
  once it happened to them). Attachments are PDF-only for documents per the
  Lane A upload limits.
- `Location`: `location.latitude` (-90…90) and `location.longitude`
  (-180…180) required; `location.label` optional (trimmed, ≤ 200 characters,
  blank becomes `null`); `text` and `attachmentId` must be absent. A Location
  message is participant-only exactly like text — the message list never
  widens who can see it.

**Idempotency in detail.** Replay matching compares the sender id plus the stored (trimmed) type,
text, location payload, and `attachmentId`; a different sender reusing a key,
or any content difference, is the frozen
`409`. Idempotency runs *before* attachment resolution, so an exact replay of
a File message returns the original without re-binding or re-validating the
attachment. A newly created message always shows `readOnUtc: null` — no
existing watermark can cover something newer than every mark already taken.

**Gate order.** Conversation existence, participation, and visibility are
checked first, then the block check, then the caller's payload — so a
non-participant probing with a deliberately malformed body still gets the
generic privacy `404`, never a validation code. A missing or unparseable JSON
body never reaches the handler: MVC model binding rejects it first with a
generic validation `400` (no Chat code).

**Block enforcement (locked generic-failure rule).** If the *recipient* has
blocked the sender, the send answers exactly the non-participant failure —
generic privacy `404`, `Chat.Conversation.NotFound` — and never a block-specific
code, so the blocked sender cannot learn that a block exists. The direction
matters: a sender who blocked the recipient (the blocker) may still send.
Unblock (endpoint 9) removes the row and is the complete off-switch.
Hidden-for-requester conversations are the same generic `404`; reopen restores
sending.

**Notification side effect (ids only).** After the message row commits, the
handler awaits a new-message in-app alert through the existing notification
seam, addressed to the non-sender. Frozen contract: the alert carries the
conversation id and message id only — never the message text (the command has
no text field at all). The stored alert's title is the fixed phrase "New chat
message" and its body repeats the ids (`Conversation: {id}. Message: {id}.`),
under idempotency key `chat-message:{messageId}:{recipientId}`, so each
recipient is alerted at most once per message. Honest seams:

- The producer fires on the **create path only**. An exact replay (`200`)
  never produces a second alert.
- The producer stages durable notification intent in the **same Chat
  transaction** as the message/change rows; a hosted dispatcher delivers it
  after commit with bounded retries (8 attempts, 5 s → 60 min jittered
  backoff), idempotency, and crash recovery (see [operations.md](operations.md)).
  A replay never loses outstanding intent, and a dispatcher restart reclaims
  stale claims — the V1 "no retry loop" residual is closed.
- The sender is never alerted.

## Unread totals — `GET /api/v1/chat/unread-count`

```json
{
  "unreadMessagesCount": 3,
  "unreadConversationsCount": 1
}
```

`200` always (a requester with nothing visible simply answers `0/0`; this
route has no `404` path). Two totals: unread *messages* — messages the
requester did not send that the read rule does not count as read — and unread
*conversations* — how many visible conversations hold at least one such
message. Conversations the requester hid are excluded entirely (a hidden
conversation is not visible anywhere, so it cannot contribute a badge;
reopening restores it). The same computation backs each conversation's
`unreadCount` field in the endpoint 1 list, so the two surfaces can never
disagree.

## The read rule (how read ticks and unread counts are computed)

Read state is per-user, one `ConversationReadState` row per (conversation,
user) from endpoint 7's `POST …/read`. A message counts as **read** for a
reader iff all of the following fail to deny it, in order:

1. The message carries its own explicit set-once `ReadOnUtc` stamp → read.
2. The reader has no read row for the conversation → not read (nothing in that
   conversation is read for them).
3. The row's `lastReadMessageId` resolves to a message **in the same
   conversation** → the mark is a watermark: everything up to and including
   that message (ordered by `createdOnUtc`, ties broken by id) is read; anything
   newer is not.
4. The row's id does not resolve inside the conversation (Lane A accepted any
   non-empty GUID and never validated it) → **timestamp fallback**: the mark's
   own `ReadOnUtc` server timestamp acts as the watermark — messages sent at or
   before it are read.

**Who the reader of an item is.** For unread counting (endpoints 1 and 8) the
reader is the requester, and only messages the requester did not send are ever
unread for them. For the `readOnUtc` tick on a message item (endpoints 3 and
4) the reader is the party who is supposed to have read it — the other
participant when the viewer sent the message, the viewer themselves when the
viewer received it. So a sender sees their outgoing message flip from
`readOnUtc: null` (blue tick pending) to a timestamp once the recipient's
watermark covers it, and a recipient sees their incoming messages' ticks
reflect their own marks. One predicate backs unread counts, `unreadCount`, and
`readOnUtc`, so no two surfaces can disagree.

Marking read (endpoint 7) never stamps message rows and never changes
`lastActivityOnUtc`, hiding, or visibility; the mark survives hide/reopen.

## Interaction with Lane A surfaces (summary)

- **Conversation list (endpoint 1)** now returns real `lastMessage`
  (`{id, type, text, createdOnUtc}`; a File message's `text` travels as `""`
  because the frozen shape carries a non-nullable string) and real
  `unreadCount` values — the Lane A stubs are gone.
- **Hide (endpoint 10)** also hides the conversation from send and from unread
  totals; a hidden-for-me conversation answers the generic privacy `404` on
  endpoints 3 and 4 until reopened.
- **Blocks (endpoint 9)** become effective at send time in the one direction
  described above; open, list, read marks, hide, upload, and serve remain
  unaffected by block state.
- **Attachments (endpoints 5/6)**: a send of `type: "File"` is what binds an
  uploaded attachment to exactly one message (`Bind()` is single-use); serving
  stays participant-only with `no-store`.
- **Eligibility open seam now wired (Opus delta + Chat Plus).** Booking opens resolve and
  validate through the backend booking gateway (see conversations.md), and the
  direct-chat role/participant matrix is enforced at open time with
  retained-history reopening after terminal bookings. The send path itself only
  enforces participant + visibility + block.

## Timestamps, ordering, retention

- Every stamp is server-set UTC. `createdOnUtc` is the send time;
  `deliveredOnUtc` is the recipient's first-fetch time (set-once); `readOnUtc`
  on the wire is the reader's mark timestamp once the read rule counts the
  message read (otherwise `null`).
- List ordering is `createdOnUtc` descending, id descending as the tie-break — a
  total order that stays stable even when server timestamps collide.
- Display ticks are presentation over these fields: sent time, delivered grey,
  read blue (frozen contract).
- **Retention is indefinite.** Nothing in Lane B deletes a message, delivery
  stamp, read state, or block row; there is no edit, no delete, no expiry, and
  no cleanup subsystem.

## Error reference (Lane B)

Every body below is `application/problem+json` with `type`, `title`, `status`,
`detail`, `instance`, `code`, and `traceId`.

| Code / condition | HTTP | Routes | Trigger |
|---|---|---|---|
| (no token or no usable `sub` claim) | `401` | 3, 4, 8 | Anonymous or unusable JWT |
| `Chat.Conversation.NotFound` | `404` generic privacy | 3, 4 | Unknown conversation id; caller not a participant; conversation hidden for the requester; **blocked sender at send time** (deliberately identical) |
| `Chat.Message.NotFound` | `404` generic privacy | 3 | `beforeId` is malformed, or names a message unknown to or outside this conversation |
| `Chat.Attachment.NotFound` | `404` generic privacy | 4 | `attachmentId` unknown, owned by another conversation, or uploaded by the other participant |
| `Chat.Message.Invalid` | `400` | 4 | Unknown/numeric `type`; empty/oversized `clientMessageId` (after trim, >100); empty/oversized `text` (after trim, >2000); `Text` carrying an `attachmentId` or location fields; `File` without one, with an empty GUID, carrying a body, or carrying location fields; `Location` without coordinates, with out-of-range coordinates, with an oversized label, or carrying a body/attachment; `attachmentId` already bound; domain backstop |
| `Chat.Message.IdempotencyConflict` | `409` | 4 | `clientMessageId` already exists in this conversation with different type/text/location/attachment |
| (MVC model-binding validation) | `400` | 4 | Missing/unparseable JSON body or non-GUID id — generic `errors` payload, no Chat code |

The three privacy codes are rewritten by the Chat controller to the generic
`404` — indistinguishable from each other *within* a route, and the raw
`Chat.*` detail text never reaches the client. Validation `400`s keep their
`code` so the sender can fix their own request, and the `409` keeps
`Chat.Message.IdempotencyConflict` so a client can key its retry logic on it.

## Privacy boundaries (summary)

- `401` for every route without a valid token; participant identity always
  from the JWT.
- Unknown, foreign, and hidden conversations, unresolvable cursors, and
  unbindable attachments are the same generic `404` — no existence, ownership,
  or reason leak; `beforeId` cannot be used as an id oracle.
- The blocked sender learns nothing: the send-time failure is byte-for-byte
  the non-participant failure.
- Notifications carry ids only; message text never reaches the notification
  seam.

All examples on this page are statically authored from the verified code paths
and lane reports (`docs/chat/reports/lane-b-*.md`); they were never executed
against a live API or database. Matching Postman requests:
[Ahlena.App.Chat.postman_collection.json](../../postman/app/Ahlena.App.Chat.postman_collection.json).
