# Chat Plus — production implementation plan, revision 2

Updated 2026-10-13 from the [frontend report](frontend-report.md) and the
mastermind review at main `41f9c91`. Owner approved builder execution on a
dedicated branch and mastermind integration. Implementation is now integrated
at correction commit `9ca63b3`; [integration evidence](reports/integration.md)
and [handoff](handoff.md) distinguish delivered work from open release/security/
storage requirements. This revision supersedes the
2026-10-12 transport/replay design and its no-migration restriction.

Existing decisions retained: one-to-one chat, REST sends, indefinite message
retention, N=5 completed paid bookings / 90 days, no voice, single-instance
initial deployment, Firebase/APNs live verification deferred until authorized.

This is the owner-accepted execution contract (Phase 0 evidence records the
approval). Section 11 retains the new rules introduced by this package.
It is a target contract, not a claim that every release gate is complete.
Do not silently
substitute different formulas or privacy behavior. [tasks.md](tasks.md) owns
execution evidence; [handoff.md](handoff.md) owns the next action.

[Additive contract details](contracts.md) pins proposed request/response shapes,
privacy settings, pricing attribution, cursor semantics, resource limits, and
backward compatibility. Phase 0 accepts these concrete choices and resolves
repository integration, rather than leaving the implementer to invent policy.

## 1. Scope and readiness

Deliver pricing insights, full SignalR with REST recovery, privacy-controlled
presence/typing, and reliable ids-only chat notifications. Harden existing
Chat prerequisites in the same package. Backend code, additive authored
migrations, automated verification, frontend integration documentation,
Postman, and operating instructions are in scope.

No voice enum, audio uploads, duration fields, group chat, message edit/delete,
Redis deployment, production credential changes, provider sends, migration
application, or deployment are authorized by this document. The report's
conditional voice suggestion does not reverse the existing exclusion.

Production readiness has two distinct evidence states:
- Implementation verified: code, reviewed migrations, contracts, automated
  gates, and client recovery harness pass.
- Release verified: exact-target migration/runtime verification, deployed
  proxy/auth/logging configuration, and Android/iPhone delivery pass.
Do not call the release verified while these external gates remain unrun.

## 2. Existing business rules to enforce

Use the approved [baseline contract](../../chat/plan.md), not the permissive
current direct-open checker:
- Family owner and all members, including Viewer, may initiate with caregivers
  and approved care homes.
- Elderly may initiate with their own family members and booking-linked
  caregivers. Resolve the Elderly identity/relationship explicitly.
- Caregivers may initiate with booking families, and reply to existing eligible
  conversations; they may not initiate arbitrary direct chats.
- Support may initiate with everyone. Other admin types do not inherit Support
  initiation or report-review powers.
- Care homes retain initiation within the approved participant matrix.
- Booking open resolves parties server-side. Reject cancelled, expired, and
  refunded bookings; caregiver-declined remains eligible per baseline.
- Cancellation does not retro-close an existing chat. Reopening an existing
  visible/hidden conversation checks membership and caller entitlement to its
  retained history; creating a new conversation checks current initiation
  eligibility. The old gateway-first implementation must not prevent retained
  history reopening solely because the booking became terminal.
- Keep pair/booking uniqueness and privacy-preserving denials. New sends fail
  generically when the recipient blocked the sender. REST history/file access
  retains the existing participant rules; realtime subscriptions add the
  stricter privacy checks in section 6.
- Hide affects only the caller. Incoming sends do not unhide; hidden
  conversations contribute no badge or realtime events for that caller.
- Support report access is reported-only, read-only for chat state, and audited.
  Uphold creates mutual user-managed blocks; this is not an account suspension.

Restricted-verification tokens are rejected for Chat and hub access; require
Normal access plus a valid JWT user ID. Eligibility uses current account and
relationship state, not the first-registered account integer used for display.
Owner must confirm the new access/reopen choices in section 11.

## 3. Pricing insights — exact proposed calculation

Route: `GET /api/v1/caregiver/pricing-insights?areaId={areaId}&serviceId={serviceId}`.
Normal authenticated caregiver access. Both IDs required, nonempty GUIDs,
existing active catalogue values; malformed/empty/inactive IDs return 400
with a documented stable validation code; missing/invalid authentication
returns 401, wrong actor/access policy 403.

Response preserves the frontend fields:
```json
{
  "currency": "EGP",
  "marketHourlyPrice": { "min": 40, "max": 80 },
  "mostRequestedEightHourDayPrice": { "min": 200, "max": 400 },
  "updatedOnUtc": "2026-10-11T08:00:00Z"
}
```
Either entire range object may be null independently. Never return fake
defaults, zero-filled ranges, or a partially null min/max object.

Proposed semantics, deliberately concrete for owner acceptance:
- Capture one injected UTC clock instant t per query. Window is
  `[t - 90 days, t]`, measured by CompletedOnUtc, inclusive at both ends.
  Return t as updatedOnUtc (calculation time, not a stored refresh timestamp).
- Eligible sample: a distinct Families caregiver booking with status Completed,
  PaidOnUtc set, valid CompletedOnUtc in the window, EGP price snapshot,
  positive BaseCaregiverFee, and reliable booked area/service attribution.
  Use booked fee snapshots, excluding platform fees/tax, not mutable current
  caregiver prices. Exclude cancelled, expired, declined, refunded, unpaid,
  malformed, or unattributable samples.
- marketHourlyPrice: Hourly shift samples only. Divide snapshotted base fee
  by booked hours; duration uses the supported overnight rule, must be
  positive, and ambiguous/invalid legacy durations are excluded. Round each
  rate to two decimals with MidpointRounding.ToEven, then take min/max.
- mostRequestedEightHourDayPrice: EightHourShift samples only, using the
  snapshotted base fee as the eight-hour day price. The name is a frontend
  field label, not a popularity ranking or a synthetic hourly-times-eight
  formula. Do not combine Medical home visits/12h/24h rates into either pool.
- Each pool independently needs at least five distinct eligible bookings.
  Four hourly plus five eight-hour samples => hourly null, eight-hour range.
  Empty pools => both null. Counts describe bookings, not joined row counts.

Current Booking has no booked area/service snapshot. Phase 0 must pin the
supported booking-to-dimension write path before implementing statistics.
Recommended contract: add nullable immutable booked area/service attribution,
record validated caller-selected IDs together at booking creation, and retain
them after catalogue/profile changes. Additive nullable fields preserve legacy
booking clients; unattributable legacy bookings return no sample. Never backfill
history from present caregiver coverage/service selections or free-text address.
Use exactly one booked service and area as specified in contracts.md; do not
expand current multi-service profiles into historical samples. The owner must
accept this booking input/schema addition or select a documented alternative
before Phase 2 starts. This is a real dependency, not an invented data source.

Execute aggregate queries in PostgreSQL, bounded by pair/window. Use decimal
arithmetic and indexes selected from the actual query plan. SQLite/InMemory
checks alone do not prove provider translation.

## 4. REST hardening and durable changes

Preserve existing wire fields and error statuses except explicit additive
contracts and access/reopen decisions above:
- Concurrent open returns the winning conversation. Concurrent identical send
  returns its original message; differing key payload returns 409. Check sender
  identity in replay equality: another participant cannot claim someone else's
  message by using its clientMessageId. Keep the existing per-conversation key.
- Catch only identified unique/concurrency conflicts, clear stale tracking,
  and re-read scoped authoritative state. Do not mask arbitrary database errors.
- Single-use attachment binding is atomic; enforce a nullable unique message
  attachment reference and appropriate FK/concurrency/conditional update.
- Read target must exist in the same conversation; invalid/foreign reference
  is a privacy 404. Watermarks only advance in server total order. Older/same
  marks succeed without regression or changed first-read timestamps.
- Delivered/read stamps are set once, concurrency-safe, and survive retries.
  First-read stamps on covered incoming messages are stable; do not regenerate
  all historical read times from the latest read watermark.
- Exactly one conditional Open-to-Dismissed/Upheld report resolution wins;
  status/audit/mutual blocks commit atomically. The loser returns 409.
- Last activity only advances; concurrent delayed saves cannot regress it.
- Calculate latest-message/unread facts in SQL; batch Identity/facility lookups.
  Add stable ID tie-break to conversation list order; paginate Support history.
- Stream bounded attachments. Enforce file size separately from request size;
  allow multipart overhead above the 50 MiB file maximum. Validate declared and
  observed size/type, structural format, and safe names. Serve PDF/video with
  safe disposition and nosniff; never expose storage keys/public bypass.
- Define per-user/per-conversation send/upload/report/connection limits in
  configuration with named defaults, 429 behavior, and tests. Limit unbound
  upload lifetime/quota; cleanup deletes only proven unbound orphan blobs,
  never bound messages/history or moderation evidence. Capture the exact
  manifest and policy before writing a cleanup worker.
- Use injected clock, finite-number coordinate validation, and explicit
  cancellation boundaries around durable commits.

Durably record message/receipt changes and notification intent in the Chat
transaction. Dispatch to notifications/realtime after commit with retries,
bounded backoff, idempotency, and recovery after crash. Cross-module persistence
must not rely on a later synchronous notification call being successful.
A replay must not lose outstanding notification intent.

Use per-conversation committed revisions or an equivalent proven commit-safe
change cursor; naive database sequence allocation can skip a later-committing
transaction. Keep a bounded event/change record with full-history reset support
before introducing pruning. Initial package retains the change log; capacity
is measured and documented. Author required additive migrations and rollback
guidance; applying them remains separately authorized.

## 5. SignalR and frontend payloads

Hub `/hubs/chat`; use standard SignalR negotiation, not a custom JWT-free
connection-ticket protocol. Send/history/upload/read/report remain REST.
User routing uses validated JWT sub, never a client-supplied user ID.

Methods:
- JoinConversation(conversationId): validate current authorization; idempotent
  join for this connection. Return a documented success/error envelope and
  current permitted counterpart presence. Rejoin after every reconnect.
- LeaveConversation(conversationId): idempotent leave; stop its typing state.
- SetTyping(conversationId, isTyping): only joined eligible participants.
  Derive userId from JWT. Maximum one accepted hint/2s/user/conversation;
  stop/disconnect clears state; expire start after eight seconds without refresh.
- Heartbeat(): derive caller/connection identity; refresh liveness, never grant
  permission or disclose arbitrary users. At most one accepted refresh/30s.

Events keep the [frontend envelopes](frontend-report.md):
- MessageCreated: { conversationId, message }. message is the complete REST
  MessageItemResponse, including File/Location payloads and nullable receipts.
  clientMessageId enables optimistic reconciliation; id deduplicates events.
- MessageReceiptUpdated: { conversationId, messageId, deliveredOnUtc,
  readOnUtc }. Nullable stamps mean absent; clients merge monotonically.
- TypingChanged: { conversationId, userId, isTyping, expiresOnUtc }.
  Stop sets isTyping=false; server expiry broadcasts stop when still eligible.
- PresenceChanged: { userId, isOnline, lastSeenOnUtc }. lastSeenOnUtc nullable
  when withheld or unknown after restart. Payload is a hint, not a durable audit.

Push only committed state; no socket event implies physical device delivery.
Delivered remains the recipient's REST fetch acknowledgement, not hub send.
Receipt updates may precede message events or repeat; frontend can hydrate via
REST and must tolerate duplicates, out-of-order events, and unknown IDs.
Internal revisions remain available in sync responses; no socket-only truth.

## 6. Authentication, authorization, privacy

Authenticate negotiation/transports with the existing access token via supported
SignalR clients. Browser WebSocket/SSE query-token support is scoped to this hub
path only; headers are used where supported. Require TLS. Redact access_token,
connection tokens, Authorization, and payloads in application/proxy/APM logs.
A SignalR connection token is not a replacement authentication credential.

Enable CloseOnAuthenticationExpiration and client refresh/reconnect. Enforce
current account/session authorization on join, hub methods, and every dispatch;
logout/revoke/block/hide/account disable closes or evicts affected connections.
Define bounded checks consistent with Identity's actual session model; never
promise immediate session revocation when the platform cannot observe it.

Groups are routing only. Resolve authorized connections before dispatch; no
unchecked group broadcast after an earlier membership check. Serialize local
permission invalidation with dispatch selection; define the boundary as no
new dispatch after committed invalidation is observed, since already-sent
network frames cannot be recalled. Remove stale connections and suppress hints.
Require exact origin allowlists; CORS alone does not protect WebSocket upgrades.

Proposed privacy settings: independent showOnlineStatus and shareLastSeen,
both default false for new and legacy users, managed by the authenticated owner.
Publish presence only to currently eligible non-hidden conversation participants
when the subject explicitly enables the relevant setting; either-direction block
suppresses all presence/typing and realtime events between that pair. Disabling
privacy withdraws previously shown state in a documented client reset/snapshot
response without leaking last seen. Never infer permission from notification
preferences, JWT role alone, or an arbitrary user ID. Support report inspection
does not grant presence subscriptions.

Presence is online if any eligible live device has heartbeat age <90s;
connection/disconnect and timeout recompute OR across devices. Store last-seen
only in memory as the last accepted heartbeat, not the timeout instant.
Restart loses presence/last seen => unknown/null until new heartbeats.
No fabricated last seen from login/device-registration timestamps.

## 7. REST sync and automatic recovery

Add `GET /api/v1/chat/conversations/{id}/sync?cursor=&pageSize=100`.
Same participant/visibility authorization, maximum page size 100.
Return { items, nextCursor, hasMore, resetRequired }; items are ordered durable
message/receipt changes with revision and type/payload. Opaque cursors are scoped
to conversation and validated; no global change feed or cross-user cursor oracle.
Pin exact DTO/error vocabulary before authoring endpoint/tests/Postman.

Initial/no cursor and explicit reset establish a paginated authoritative snapshot
plus a consistent high-water revision; subsequent reads consume committed changes.
The implementation must prove snapshot-to-change handoff cannot miss concurrent
commits. Malformed/foreign cursor => privacy 404; expired retained cursor =>
resetRequired=true, no silent newest-page truncation. Complete the snapshot and
then replay changes. Preserve beforeId exclusively for older history; do not use
it as a forward replay cursor and do not invent an unimplemented since parameter.

Use the same sync flow for sockets, degraded polling, app foreground, and restart:
1. Start connection/event buffering, join authorized conversations.
2. Complete paginated REST sync; apply durable changes in order, deduplicate
   by message ID/revision, then merge buffered hints/events and sync again.
3. On outage use REST sync every 5s for open conversations while foreground;
   refresh conversation list/unread every 15s. Back off failures with jitter
   1s to 30s; stop blind retries on 401/403 and reconcile authorization.
4. Reconnecting always rejoins and syncs from last applied cursor.
   Do not advance cursor until its whole page is applied.
5. Background pauses foreground polling; OS push is advisory. Foreground sync
   always runs even if no push arrived. Typing/presence are cleared when stale.

Allow WebSockets, SSE, Long Polling where supported; document SDK/platform
capabilities and test permitted fallback rather than assume every client has
SSE. Initial connection failures and transport failures require explicit client
retry logic; REST fallback is a frontend responsibility with a runnable harness.

## 8. Notifications, Firebase, APNs

Fix the concrete producer/fan-out mismatch: Chat/NewMessage currently does not
match MessagesFromFamilies. Keep canonical Chat/NewMessage; explicitly route
it and test an actual producer-to-outbox path with a registered device.

Proposed preference: ChatMessages for all chat sender types, default enabled,
with backward-compatible legacy read preserving the existing
MessagesFromFamilies opt-out for old users. Pin full-replacement preference
payload compatibility and migration behavior before implementation. This is a
new product choice, not a silent rename/broadening of the old preference.
Check current block/visibility/account eligibility and preference at dispatch;
no message text or location in notifications, push data, metrics, or logs.
Push includes conversation/message IDs and stable dedupe identity; navigation
uses the established deep-link contract with authorized REST hydration.

Use existing FCM sender, device registration, and outbox, rather than duplicate
provider infrastructure. Keep generic notification title/body. Distinguish
unconfigured, invalid token, payload/configuration failure, transient provider
failure, and cancellation; do not retire all tokens for any InvalidArgument
without proving a token-specific failure. Bound retries and expose sanitized
delivery/configuration health to operators.

Release configuration:
- Backend Firebase service account/project via secret injection only.
- Firebase Apple app with correct bundle ID; APNs authentication key configured
  in the matching Firebase project. Apple credentials do not belong in API
  source/Postman/database fixtures.
- iOS owner enables Push Notifications and required background capabilities,
  initializes FCM/APNs integration, requests permission, and registers the FCM
  token through existing devices API; handle token refresh/logout.
- Verify Android and physical iPhone foreground/background taps, permission
  denied, token refresh, logout/block/disabled preferences, provider retries,
  correct environment/project, and no-content disclosure.
External setup/sends remain owner-operated until explicitly authorized.
Live proof deferred means release gate incomplete, not that push is unnecessary.

## 9. Deployment and operating limits

Initial supported topology is one API instance. In-memory connection/presence
registries behind a dedicated seam; durable event dispatch behind a separate
transport seam. Do not conflate a local registry with a Redis backplane.
Multi-instance without a verified backplane is unsupported: realtime messages,
receipts, typing, and presence all split, although REST persistence remains safe.
Fail validation for a declared multi-instance topology lacking configured support.

Record reverse-proxy WebSocket upgrade/timeout/TLS settings, origin allowlist,
query-token redaction, request/upload limits, health and secret configuration,
migration ordering, graceful shutdown, and rollback compatibility. Monitor
dispatch backlog age, retries/failures, sync latency, query costs, active
connections, rejected joins, and rate limits without PII/message payloads.

## 10. Acceptance and verification

Automated gates after final code: zero-warning build, focused domain/handler
and real relational concurrency/provider tests, then full unit and architecture
once for this cross-cutting change; Postman parse, diff check, route mapping.
Do not replace PostgreSQL tests with only EF InMemory or assume SQLite UUID/
timestamp/transaction behavior proves Npgsql behavior.

Required scenarios:
- Approved actor matrix, Elderly linkage, terminal booking retained-history
  reopening, restricted token denial, foreign IDs/cursors, hidden/block policy.
- Concurrent identical/conflicting sends, opens, reads, single-use attachments,
  report dismiss/uphold, activity monotonicity, stable first receipt timestamps.
- Notification crash between commits, dispatcher restart, failed/repeated
  delivery, replay recovery, actual Chat producer-to-push mapping.
- Pricing pools 0/4/5, independent nulls, exact window boundaries, immutable
  attribution, duplicate joins, overnight hourly fee/rounding, unpaid/refunded
  exclusions, equality with direct PostgreSQL aggregates.
- Two clients: message/receipt push; token expiry/logout; join/block/hide during
  connection; typing expiry; multi-device OR; both privacy settings; restart.
- Kill socket and create >100 missed messages AND receipt-only changes; full
  sync converges, including concurrent commits during snapshot/page handoff.
  Test initial socket failure, Long Polling, REST-only fallback, and foreground.
- Upload maximum plus multipart overhead, observed-size mismatch, forged
  signature/invalid structure, public-path denial, resource limits and cleanup.
- Pagination/performance with retained histories and moderate concurrency;
  publish measured limits, not an untested throughput guarantee.

Read-only/scoped code tests are separate from live exact-target gates.
Migration application, fixture writes, API startup side effects and provider
sends require their existing owner authorizations. Record authored versus run
checks, revision, target, counts, warnings, skips, and remaining limitations.

## 11. New proposed rules — owner acceptance before execution

1. Normal-only chat access and retained-history reopen after booking cancellation.
2. Snapshot-based area/service attribution; per-pool N=5; hourly-only and
   EightHourShift-only ranges; base fees; UTC completion window; rounding as above.
3. Monotonic valid read watermarks and stable first receipts; sender-bound replay.
4. Durable change sync/outbox and required additive migrations.
5. Presence/last-seen opt-in settings default false; either-direction block
   suppresses realtime; unknown last seen is null.
6. Canonical Chat push mapping and backward-compatible ChatMessages preference.
7. Bounded upload/orphan policy, report pagination, and configured abuse limits.
8. Single-instance release only until backplane evidence exists.
9. Frontend owns reconnect/polling/reconciliation implementation; backend supplies
   contracts and a runnable integration harness.
10. Firebase/APNs preparation is in scope; actual live verification is an
    outstanding release gate until authorized.

Phase 0 turns these proposed rules into accepted execution contracts and pins
exact file manifests/DTOs/configuration limits/data write paths. If the owner
changes any proposal, update this one plan and dependent tasks before code.
No unresolved statistical attribution or privacy decision is called blocker-free.

## References

- [Frontend report](frontend-report.md)
- [Additive contract details](contracts.md)
- [Baseline chat contract](../../chat/plan.md)
- [SignalR authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0)
- [SignalR security](https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0)
- [Firebase Apple setup](https://firebase.google.com/docs/cloud-messaging/ios/get-started)
