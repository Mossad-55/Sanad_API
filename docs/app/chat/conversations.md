# Chat API — conversations, attachments, read marks, blocks, hiding

All Chat app routes require an authenticated account and live under
`/api/v1/chat`. The routes carry `[Authorize]` with no policy, so any
signed-in account may call them; the acting participant always comes from the
JWT `sub` claim and can never be supplied by the client. Anonymous callers
receive `401`.

This page specifies Lane A of the frozen contract in `docs/chat/plan.md`:
endpoints 1 (list), 2 (open-or-get), 5 (attachment upload), 6 (attachment
serve), 7 (read mark), 9 (block/unblock/list), and 10 (hide) — with the Opus
deltas applied (exactly-one open target with backend booking resolution,
participant `accountType` + facility display, `createdOnUtc` on message
timestamps, attachment serve by attachment id, read mark over `POST`).
Messages, unread counts, and reports (endpoints 3–4, 8, 11) plus the
`/api/v1/admin/chat` support routes (12–15) are specified in `messages.md` /
`reports.md`.

**Privacy posture (applies to every route below).** An unknown id, a foreign
row, and an ineligible row are indistinguishable: they answer `404` with the
generic problem body (`title: "Not Found"`,
`detail: "The requested resource was not found."`). The `code` extension still
names the failing gate family — `Chat.Conversation.NotFound`,
`Chat.Attachment.NotFound`, `Chat.Block.NotFound` — but never *why* it failed:
"does not exist", "not yours", and "not eligible" are one and the same code,
and the route already tells the caller which resource kind was addressed.
Failures of the caller's *own* request (bad file, empty read reference,
self-block, both/neither open target) are `400`s carrying the specific
`Chat.Attachment.*`, `Chat.ConversationRead.Invalid`,
`Chat.Conversation.OpenTargetInvalid`, or `Chat.Block.Invalid` code: the
caller is entitled to know their own request was invalid.

## Conversations

### List — `GET /api/v1/chat/conversations?page=1&pageSize=20`

Returns the caller's conversations, last activity first, as a `PagedResult`.
Only rows where the caller is a participant appear, minus rows the caller has
hidden; foreign conversations are simply absent, never an error.

```text
GET /api/v1/chat/conversations?page=1&pageSize=20
```

- `page` is clamped up to a minimum of 1; `pageSize` is clamped to 1–100
  (default 20, values above 100 return 100 rows).
- Ordering is `LastActivityOnUtc` descending. No secondary sort key is
  defined for equal timestamps.
- `totalCount` counts all visible rows; `totalPages` is
  `ceil(totalCount / pageSize)`.

```json
{
  "items": [
    {
      "id": "0198e2c2-4444-7a10-8000-00000000c001",
      "participant": {
        "userId": "00000000-0000-0000-0000-000000000002",
        "displayName": "Salma Nasr",
        "avatarUrl": null,
        "accountType": 1
      },
      "lastMessage": null,
      "unreadCount": 0
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

`participant` is always the *other* side of the 1:1 pair, resolved through the
Identity profile lookup: `displayName` prefers the English full name and falls
back to Arabic (an unresolvable profile yields an empty string, and
`avatarUrl` stays `null`). `accountType` is the Identity `AccountType`
integer of the counterpart's first-registered account (`null` only when their
Identity row is gone). A care-home owner presents as the facility instead of
the owner: `displayName` is the facility name (English preferred, Arabic
fallback) and `avatarUrl` is its approved cover image, while `userId` stays
the owner's id so messaging and blocking keep working. **Lane A stub:**
`lastMessage` is always `null` and `unreadCount` is always `0` — the Messages
module does not exist in this lane. Both field names are already frozen by the
contract and are wired to `Message` when Lane B lands.

### Open-or-get — `POST /api/v1/chat/conversations`

Opens a conversation, or returns the existing one. Always `200` — this route
never creates a duplicate. Exactly one target is required (Opus delta): a
direct chat names the recipient, a booking chat names the booking and the
backend resolves the counterpart.

```json
{ "recipientUserId": "00000000-0000-0000-0000-000000000002" }
```

```json
{ "bookingId": "00000000-0000-0000-0000-0000000000b1" }
```

`{ "conversationId": "...", "created": true }` means new history;
`created: false` means the existing row was returned.

```json
{
  "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
  "created": true
}
```

A booking open carries `scope = Booking`; a direct open carries
`scope = Direct`. Both/neither target is `400
Chat.Conversation.OpenTargetInvalid` (the caller's own malformed request —
it leaks nothing about any resource).

**Eligibility matrix as implemented.** Eligibility is evaluated at open time,
before the duplication lookup, and every denial below collapses into the same
generic privacy `404` (`Chat.Conversation.NotFound`) so the caller can never
tell which check failed:

| Condition at open time | Result |
|---|---|
| Both `recipientUserId` and `bookingId` set, or neither | `400 Chat.Conversation.OpenTargetInvalid` |
| `recipientUserId` equals the caller (self-open) | `404` |
| `recipientUserId` empty, unknown, or no longer resolvable | `404` |
| `bookingId` present but `00000000-0000-0000-0000-000000000000` | `404` |
| `bookingId` unknown, or the caller is not a party to the booking, or the booking is cancelled/expired/refunded | `404` |
| Recipient resolves and pair is not self | `200` |

Booking validity is enforced by the backend booking gateway: a Families
booking is eligible unless `CancelledByFamily`, `CancelledByCaregiver`,
`Refunded`, or `Expired` (a caregiver-*declined* booking stays eligible);
a care-home booking is eligible unless `Expired`, `Cancelled`, or `Refunded`.
Family-side involvement covers the owner and every member; from the caregiver
side the counterpart resolves to the booking creator. For care-home bookings
the two sides are the family user and the facility owner. Whether the
counterpart resolves to "the other user on the booking" is the complete rule
— no additional role matrix applies to booking opens.

**Starting a booking-less care-home chat.** A direct chat with a facility
names the facility owner's user id as `recipientUserId`. Clients obtain it
from facility detail — `GET /api/v1/care-homes/discovery/{careHomeId}`
carries the additive `contactUserId` for authenticated callers (`null` for
anonymous visitors) — then open with `{ "recipientUserId": "<contactUserId>"
}`. The counterpart then presents as the facility (name/cover) in the
conversation list.

**Eligibility not yet enforced (remaining gap).** The following locked rule
from `docs/chat/plan.md` is *not* checked and must not be assumed by clients
or tests:

- The direct-chat role/participant matrix (family → any caregiver, booked
  caregivers, any care home; elderly → own family members and booking-linked
  caregivers; support → everyone; caregiver initiation limited to booking
  families plus replies; all family roles including Viewer participate).
  Consequence today: any two distinct registered users can open a direct
  conversation with each other, including pairings the plan restricts (e.g.
  elderly ↔ caregiver without a linking booking).

**Duplication and reopening (matches the frozen rules).** The conversation key
is the normalized (ordered) participant pair plus the booking reference:

- Same pair + same booking → one conversation; reopening returns that id with
  `200` and `created: false`.
- Same pair without a booking → one direct conversation.
- A booking-scoped row and a direct row between the same pair are *different*
  conversations, and so are two rows carrying two different booking ids.
- Eligibility never applies retroactively: there is no close operation at all,
  so a cancelled booking can never retro-close an existing chat.

**Reopen restores visibility for the opener only.** If the caller had hidden
the conversation, a successful open un-hides it for the caller alone: the
other party's hidden flag and `LastActivityOnUtc` are untouched, and no read
mark is created or cleared.

### Hide — `DELETE /api/v1/chat/conversations/{conversationId}` → `204`

Hide is **not** delete. It flips a per-user visibility flag for the caller
only:

- The other party keeps the conversation and its full history.
- Nothing is erased. There is no erasure path in Lane A (or in the V1
  contract); retention is indefinite.
- Idempotent: hiding an already-hidden conversation still answers `204`.
- The row disappears from the caller's `GET conversations` list and reappears
  only after a successful reopen (open-or-get for that same pair + booking).
- Hiding is not activity: `LastActivityOnUtc`, read marks, and the other
  party's view are unaffected.
- Unknown or foreign conversation ids answer the generic privacy `404`.

## Attachments

### Upload — `POST /api/v1/chat/conversations/{conversationId}/attachments`

`multipart/form-data` with a single `file` field (`415` if the request is not
multipart). The conversation must exist and the caller must be a participant;
that gate runs **before** any file validation, so a stranger probing an
unknown conversation with a deliberately invalid file still gets the generic
privacy `404`, never a validation code.

Success is `201`:

```json
{
  "attachmentId": "0198e2c2-4444-7a10-8000-00000000a001",
  "fileName": "referral.pdf",
  "contentType": "application/pdf",
  "sizeBytes": 184320
}
```

`fileName` is the sanitized echo of the caller's original name (trimmed, path
segments collapsed to the last segment, fallback `file` when blank); the
stored object always uses a generated safe key and the original name is never
persisted.

**Validation limits by type.** The declared content type is normalized
(trimmed, lowercased) and looked up; anything else is rejected:

| Content type | Maximum size |
|---|---|
| `image/jpeg`, `image/png`, `image/webp` | 5 MB (5,242,880 bytes) |
| `video/mp4` | 50 MB (52,428,800 bytes) |
| `application/pdf` | 10 MB (10,485,760 bytes) |

Documents are **PDF-only** in Lane A (owner-approved; `doc`/`docx` deferred to
provider work). Voice notes and signed URLs do not exist in V1. Upload
failures answer `400` with:

- `Chat.Attachment.UnsupportedType` — content type outside the allowlist.
- `Chat.Attachment.TooLarge` — file exceeds the per-type limit above.
- `Chat.Attachment.Invalid` — missing `file` part, zero-length file, declared
  length not matching the received bytes, magic-byte/signature mismatch, or a
  failed upload-row persistence (the stored blob is removed in that case).

**Magic-byte verification.** A matching declared content type alone is never
trusted: JPEG must start `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP
`RIFF….WEBP`, MP4 `ftyp` at offset 4, PDF `%PDF`. Storage-layer failures are
translated into `Chat.Attachment.*` codes — callers never see `Storage.*`
codes.

**Transport cap.** The endpoint carries a request size limit of 52,428,800
bytes for the whole multipart body; larger requests are rejected by the host
before the handler runs (`413`/connection reset depending on server), which is
slightly stricter than the per-type `TooLarge` check because multipart
overhead counts toward the cap.

**Binding and retention.** An upload is conversation-scoped and
uploader-bound, and is created *unbound*: it binds to exactly one message when
Lane B's send path runs the single-use domain `Bind()` (a second bind throws).
Files live under private storage with generated keys and are never served by
the static `/files` host. There is no cleanup subsystem, so uploads that are
never sent remain stored.

### Serve — `GET /api/v1/chat/attachments/{attachmentId}/file`

Returns the raw bytes for participants only, with the stored content type and
`Cache-Control: no-store` + `Pragma: no-cache`. `401` for anonymous callers.
The route carries the attachment id only: the participant check runs against
the conversation the attachment belongs to, so the client can never steer it
with a conversation id.

Every denial collapses into the same generic privacy `404`
(`Chat.Attachment.NotFound`): unknown attachment id, a foreign requester, or
a missing blob. There is no way to tell which check failed, and there are no
signed URLs in V1 — authorized serving happens only through this controller.

## Read marks — `POST /api/v1/chat/conversations/{conversationId}/read`

```json
{
  "lastReadMessageId": "0198e2c2-4444-7a10-8000-00000000b001"
}
```

Success is `204`, idempotent: one row per (conversation, user), so repeating
the call updates the same row and refreshes its `ReadOnUtc` stamp — never a
duplicate row, never a failure.

**Marks are recorded verbatim (Lane B seam).** Lane A has no `Message` table,
so it cannot and does not validate the message id: any non-empty GUID is
accepted, even one that never existed. Unread counts, read ticks, and any
"must be the newest message" rule are Lane B's to interpret from the stored
`ConversationReadState`. Consequences:

- `400 Chat.ConversationRead.Invalid` is returned *only* to a participant who
  sent an empty/missing `lastReadMessageId`.
- The conversation/participant gate runs before payload validation, so a
  non-participant always gets the generic privacy `404` — even with a
  deliberately malformed body. The body can never be used as an existence
  oracle.
- A missing or unparseable request body never reaches the handler: MVC model
  binding rejects it first with a generic validation `400` (`errors` payload,
  no Chat code) — the same answer whether or not the conversation exists.

A read mark never changes `LastActivityOnUtc`, hiding, or visibility, and it
survives hide/reopen untouched (neither side creates or clears a mark).

## Blocks — endpoint 9

```text
POST   /api/v1/chat/blocks          { "blockedUserId": "…" }  → 204
DELETE /api/v1/chat/blocks/{userId}                            → 204
GET    /api/v1/chat/blocks                                     → 200
```

- **Block** is per-user and idempotent. `204` on first block and on repeats
  (the unique pair index is the database backstop).
- **Unblock** is always `204`, even when no row existed: it is the blocker's
  own action, and always-success reveals nothing about whether a block was
  ever in place. Removing the row is the complete off-switch for sending.
- **List** returns the caller's own blocked user ids only — never ids blocked
  by anyone else — ordered most-recent block first (`CreatedOnUtc` desc):

```json
["00000000-0000-0000-0000-000000000009", "00000000-0000-0000-0000-000000000007"]
```

Failures on `POST /blocks`:

- `400 Chat.Block.Invalid` — self-block, or an empty/absent `blockedUserId`
  from a malformed body (the caller's own mistake, revealed back to them).
- `404` generic privacy (`Chat.Block.NotFound`) — the target id is not a
  registered user, so this route can never be used to probe registrations.

**Generic-failure rule (Lane B seam, locked).** Block state is *not* consulted
by any Lane A route: open, list, read, hide, upload, and serve behave exactly
the same whether or not the pair has a block. The rule bites at send time
(Lane B's endpoint 4): a blocked sender must receive **exactly** the same
failure as a non-participant — `Chat.Conversation.NotFound` forced into the
generic privacy `404` — and never a block-specific code, so the blocked sender
never learns that a block exists. Blocking is directional: a `(blocker,
blocked)` row denies sends *from* the blocked user *to* the blocker, while the
blocker may still send. Unblock removes the row, which is the only state Lane B
consults, so unblocking restores sending. Lane A has no send path; the rule is
asserted as state (the predicate is queryable, unblock clears it).

## State transitions (Lane A)

| Entity | States and transitions |
|---|---|
| Conversation | created by open (`created: true`) → visible to both → hidden-for-me by `DELETE` (peer unaffected) → reopened by open-or-get (`created: false`, unhidden for me only). No close, delete, or erase state exists. |
| Attachment | uploaded, unbound → (Lane B) bound to exactly one message → served to participants. `IsBound` can only flip once. |
| Read state | absent → marked (`204`, `ReadOnUtc` stamped) → re-marked (same row, stamp refreshed). Never deleted by Lane A. |
| Block | absent → blocked (`204`) → unblocked (`204`, row removed). |

## Timestamps, ordering, retention

- Every stamp is server-set UTC. Clients cannot supply timestamps.
- `Conversation.CreatedOnUtc` and `LastActivityOnUtc` are set to open time.
  `LastActivityOnUtc` is bumped only by the message flow (Lane B's
  `UpdateLastActivity` seam); read marks, hides, reopens, and blocks never
  touch it — this is what keeps list ordering stable.
- List ordering: `LastActivityOnUtc` descending; blocked-user list ordering:
  `CreatedOnUtc` descending (most recent first).
- `ConversationReadState.ReadOnUtc` is the server time of the latest mark.
- `Attachment.UploadedOnUtc` is the upload time (not echoed in the `201`
  body); `Block.CreatedOnUtc` is the block time.
- Display ticks for messages (sent time, delivered grey, read blue) are Lane B
  presentation over `createdOnUtc`/`deliveredOnUtc`/`readOnUtc`; Lane A exposes
  no message timestamps.
- **Retention is indefinite** (locked). Nothing in Lane A deletes a
  conversation, message, attachment, read mark, or block row; hiding and
  unblocking only flip visibility/state, and orphaned uploads are retained
  because no cleanup subsystem exists.

## Error reference (Lane A)

Every body below is `application/problem+json` with `type`, `title`, `status`,
`detail`, `instance`, `code`, and `traceId`.

| Code / condition | HTTP | Routes | Trigger |
|---|---|---|---|
| (no token or no usable `sub` claim) | `401` | 1, 2, 5, 6, 7, 9, 10 | Anonymous or unusable JWT |
| `Chat.Conversation.NotFound` | `404` generic privacy | 2, 5, 7, 10 | Self-open; unknown/deleted/empty recipient; `bookingId` = `Guid.Empty`; unknown booking, caller not a party to the booking, or cancelled/expired/refunded booking; unknown conversation id; caller not a participant (upload, read, hide) |
| `Chat.Conversation.OpenTargetInvalid` | `400` | 2 | Both or neither of `recipientUserId`/`bookingId` supplied |
| `Chat.Attachment.NotFound` | `404` generic privacy | 6 | Unknown attachment id, foreign requester, missing blob |
| `Chat.Block.NotFound` | `404` generic privacy | 9 (POST) | Target user id is not registered |
| `Chat.Attachment.Invalid` | `400` | 5 | Missing `file` part, zero-length file, declared length ≠ received bytes, magic-byte mismatch, upload-row persistence failure |
| `Chat.Attachment.TooLarge` | `400` | 5 | File exceeds the per-type limit (5 MB image / 50 MB video / 10 MB PDF) |
| `Chat.Attachment.UnsupportedType` | `400` | 5 | Declared content type outside `image/jpeg`, `image/png`, `image/webp`, `video/mp4`, `application/pdf` |
| `Chat.ConversationRead.Invalid` | `400` | 7 | Participant sent empty/absent `lastReadMessageId` |
| `Chat.Block.Invalid` | `400` | 9 (POST) | Self-block, or empty/absent `blockedUserId` |
| (MVC model-binding validation) | `400` | 2, 7, 9 (POST) | Missing/unparseable JSON body or non-GUID id — generic `errors` payload, no Chat code |
| (unsupported media type) | `415` | 5 | Request is not `multipart/form-data` |
| (request body over the 50 MB host cap) | `413` / connection reset | 5 | Whole multipart body exceeds 52,428,800 bytes |

The three privacy codes are rewritten by the Chat controller to the generic
`404` (`title: "Not Found"`, `detail: "The requested resource was not found."`)
— they are indistinguishable from each other *within* a route, and the raw
`Chat.*` detail text never reaches the client. Validation `400`s keep their
`code` so the uploader/reader/blocker can fix their own request.

## Privacy boundaries (summary)

- `401` for every route without a valid token; participant identity always
  from the JWT.
- Unknown, foreign, and ineligible conversations/attachments/attachment files
  are the same generic `404` — no existence, ownership, or reason leak.
- Block state is never revealed to the counterpart: no block-specific code on
  any route, unblock always `204`, blocked-list is per-user, and the send-time
  failure (Lane B) is byte-for-byte the non-participant failure.
- Support routes are a separate surface: the `ChatSupportAdmin` policy
  (SuperAdmin + SupportAdmin) belongs to endpoints 12–15 (Lane B), and support
  sees reported conversations only — there is no non-participant read path in
  Lane A at all.

All examples on this page are statically authored from the verified code
paths and lane reports (`docs/chat/reports/lane-a-*.md`); they were never
executed against a live API or database. Matching Postman requests:
[Sanad.App.Chat.postman_collection.json](../../postman/app/Sanad.App.Chat.postman_collection.json).
