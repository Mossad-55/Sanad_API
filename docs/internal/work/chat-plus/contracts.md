# Chat Plus — proposed additive contract details

Read with [plan.md](plan.md). These are concrete recommended defaults for
Phase 0 acceptance, not claims about existing endpoints or owner-approved
behavior. Any adjustment updates this file, plan, and affected tests together.

## Pricing attribution

On supported caregiver booking creation add nullable `bookingAreaId` and
`bookingServiceId`; require both together or neither. Both supplied IDs must
be active catalogue entries selected by that booked caregiver at creation.
Persist their immutable IDs on the booking in the same creation transaction.
Neither supplied preserves legacy checkout behavior but contributes no pricing
sample. One supplied, empty GUID, unknown ID, or unselected pair returns 400
through the owning booking validation vocabulary. No location inference,
current-profile backfill, or multi-service expansion. One booking contributes
to one pair and at most one eligible shift pool. Phase 0 locates every supported
creation path and prevents inconsistent attribution through alternate paths.

Proposed pricing query codes: `Caregivers.PricingInsights.InvalidFilter` for
missing/malformed/empty/unknown/inactive IDs; 401/403 use existing auth behavior.
Normal authenticated caregiver policy includes Medical and Companion accounts.
No data is 200 with null ranges. Use one database snapshot/read transaction for
both pools so the response does not mix different concurrently committed data.

## Presence privacy API

Add owner-only Normal-access endpoints:

- `GET /api/v1/chat/privacy` -> 200
- `PUT /api/v1/chat/privacy` -> 200, full replacement; both Boolean fields
  required, not defaulted by omitted JSON fields

```json
{ "showOnlineStatus": false, "shareLastSeen": false }
```

Default both false for legacy/new users. Store owner settings durably; only
liveness/last seen are in-memory. No caller-supplied owner ID. A change evicts
or refreshes affected subscriptions before further dispatch. Only authorized
joined conversations subscribe to counterpart presence.

Add an authorized presence snapshot:
`GET /api/v1/chat/conversations/{id}/presence` -> 200

```json
{
  "userId": "uuid",
  "isOnline": null,
  "lastSeenOnUtc": null
}
```

`isOnline` is nullable in the snapshot: null means undisclosed/unknown, not
offline. The event keeps Boolean `isOnline`; clients must apply the snapshot
after privacy changes, authorization changes, foreground, and reconnect.
When online sharing is off but last-seen sharing is on, provide permitted last
seen in this snapshot; do not emit an online-state event to leak liveness.
The frontend clears stale dots/last-seen on snapshot nulls. PresenceChanged
has no ability to grant a new subscription. Either-direction block suppresses
both fields without removing existing REST history access.

## Sync response and cursor semantics

`GET /api/v1/chat/conversations/{id}/sync?cursor=&pageSize=100`

```json
{
  "items": [
    {
      "revision": "42",
      "type": "MessageReceiptUpdated",
      "payload": {
        "conversationId": "uuid",
        "messageId": "uuid",
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

Revision is a decimal string to avoid JavaScript integer precision loss.
MessageCreated payload is exactly the frontend envelope with the complete
REST message item. The cursor is authenticated/versioned and scoped to the
conversation, requester, and snapshot/change mode. It contains the applied
revision and snapshot boundary/position where needed; clients never construct
or edit it. A caught-up page still returns nextCursor for the next poll.
No/empty cursor starts the consistent snapshot. All snapshot pages share a
captured revision boundary; pages enumerate immutable messages in stable total
order and then consume changes beyond that boundary. Concurrent receipt changes
are recovered by replay, not assumed frozen by the snapshot. Receipt reads in
sync can acknowledge only actual message snapshots delivered to the recipient,
never merely a revision number or a receipt event. Unchanged receipts produce
no new event, preventing polling-generated loops.

Malformed/foreign/requester-mismatched cursor is generic privacy 404. An
authentic but no-longer-retained/version-supported cursor returns an empty page
with resetRequired=true and nextCursor=null; client starts a new snapshot.
Unknown conversation/foreign/hidden caller is the usual privacy 404. Page size
clamps 1–100, default 100. Server cursor validation never reveals another
conversation's existence. Never truncate recovery to the newest 100 rows.

## Hub method outcomes

Join/leave return `{ "joined": true }` or `{ "joined": false }` respectively;
idempotent repeats return the same state. Ineligible join throws a sanitized
hub error with `Chat.Conversation.NotFound`, never an existence/block reason.
SetTyping/Heartbeat return no content. Unjoined, malformed, ineligible or
rate-limited hints are dropped with sanitized counters; expiry is server-set.
Do not add free-form error details, message content, or token values to errors.

## Proposed resource defaults

All limits are configurable, apply across the user's devices on the supported
single instance, and use the shared injected clock. Acceptance tests verify
boundary and renewal behavior. Expensive writes rejected before full buffering.

| Resource | Proposed default | Outcome |
|---|---|---|
| New sends | 60/min/user and 30/min/user/conversation | HTTP 429 + Retry-After |
| Opens | 30/min/user | HTTP 429 + Retry-After |
| Uploads | 10/min/user, 2 concurrent/user | HTTP 429 + Retry-After |
| Reports | 5/hour/user | HTTP 429 + Retry-After |
| REST sync | 60/min/user/conversation, 300/min/user | HTTP 429 + Retry-After |
| Hub negotiate | 30/min/user plus existing ingress IP controls | HTTP 429 |
| Hub connections | 5/user; 20 joined conversations/connection | Sanitized rejection |
| Hub inbound frame | 16 KiB | Reject/close oversized frame |
| Typing refresh | 1/2s/user/conversation | Drop excessive hints |
| Accepted heartbeat | 1/30s/connection | Drop excessive refresh |
| Unbound attachments | 100 MiB/user (all unbound bytes, expired or not, while their rows exist); expires after 24h | Reject over quota; safe cleanup |
| Cleanup sweep | Every hour, max 100 objects/pass | Retry failures; never bound files |
| Multipart request | 52 MiB total, one file; MP4 remains <=50 MiB | HTTP 413 over request limit |
| Outbox attempts | 8; exponential 5s to 60min with jitter | Durable Failed + operator retry |
| Dispatch batch | 100, one active claim/item; stale claim 5min | Reclaim safely/deduplicate |
| Support history page | 30 default, clamp 1–100 | Stable cursor page |

Apply a fixed rolling-window definition to HTTP limits in the execution report;
use a sliding 60-second/hour window (as indicated above), no reset burst at
clock-minute boundaries; Retry-After is the earliest token/window release.
Preserve Retry-After and avoid hidden-account/resource details. Quota binding
and cleanup claims must be serialized with send: cleanup cannot delete a file
that a concurrent transaction has bound (implemented as a conditional
`UPDATE … WHERE IsBound = FALSE` bind via the IsBound concurrency token plus a
`SELECT … FOR UPDATE SKIP LOCKED` sweep claim and conditional delete).
Expiry affects unbound uploads only; expired attachment sends return the
generic attachment-not-found response. The quota counts every unbound row
because a row exists exactly while its blob is physically occupied — expired
bytes still count until the sweep physically deletes them, so successive
logical expiry cycles cannot grow disk without bound.

For compatibility retain the existing Support history array endpoint for
conversations with at most 1,000 messages. Above that bound return 409 with
`Chat.Report.HistoryRequiresPagination`, never a truncated-success array.
Add `/api/v1/admin/chat/reports/{id}/messages/page` returning the existing
MessageCursorPage shape, with read-only behavior and beforeId pagination.
Mark the array endpoint deprecated and migrate the admin client before removing
it. This bounded compatibility behavior is a new proposed rule for Phase 0
acceptance. Do not silently change its response from array to object.

## Push preference compatibility

Add `chatMessages` to the existing account preference response. Make it
optional in the full-replacement update request during compatibility rollout.
Explicit chatMessages wins. If omitted and messagesFromFamilies changes from
its stored value, mirror that legacy change to chatMessages; if neither changes,
preserve chatMessages. Omission alone never resets an opt-out. Initialize new
users enabled. For legacy users migrate its value from messagesFromFamilies;
keep that existing field for its existing consumers. Subsequent chat pushes
consult chatMessages. Test both old/new clients, conflicting explicit fields,
omitted chatMessages and no-op legacy updates. Retain the adapter until a
separately agreed client compatibility rollout permits its removal.

Provider payload includes string conversationId/messageId, notificationId,
entity ID/kind and established inbox deep link. Client opens authorized history;
no text/file/location in data. Changing direct navigation requires mobile-team
agreement; do not replace the existing URI scheme silently.
