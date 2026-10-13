# Chat Plus — Phase 1 evidence: REST correctness, privacy, resource bounds

Base: `41f9c91`. Branch: `builder/chat-plus-production`.
Status: implemented + focused-tested. Migrations authored-only, never applied.

## Changed manifest (Phase 1)

Domain: `Conversations/Conversation.cs` (`ChangeRevision` token, monotonic
activity, `TakeNextRevision`), `Reports/Report.cs` (`Version` token),
new `Changes/`, `Outbox/`, `Privacy/` entities + id structs.
Application: `IChatDbContext` (+3 DbSets); new `Events/`, `Persistence/`,
`Privacy/`, `RateLimiting/`, `Realtime/` (seams), `Sync/`, `Presence/`,
`Reports/GetReportMessagesPageQuery.cs`; rewritten/extended open, send,
read, history, list, unread, upload, submit/dismiss/uphold report handlers;
batch defaults on `IChatUserLookup`/`IChatFacilityLookup`; reopen record on
`IChatBookingGateway`.
Infrastructure: `ChatDbContext`, 3 edited + 3 new configurations, DI
(rate options, gate singleton, recorder scoped), migration
`20261013000659_AddChatPlusChanges` (authored, unapplied).
API: `ApiChatEligibilityChecker` (matrix), `ApiChatBookingGateway`
(reopen + elderly), `ApiChatUserLookup` (active-only, current types,
batch), `ApiChatFacilityLookup` (batch), `DataProtectionChatSyncCursorProtector`,
`ChatController` (NormalAccess, 429, sync/privacy/presence, upload slot),
`AdminChatReportsController` (paged endpoint, 409 compat), DI registration,
`Controllers/Requests/ChatUpdatePrivacyRequest.cs`.
Shared: `Error` gains optional `RetryAfter` (additive; all existing
`new Error(code, msg)` call sites compile unchanged).
Tests: new `tests/Sanad.UnitTests/ChatPlus/` (22 facts); updated
pre-existing Chat tests pinning superseded behavior (read validation,
attachment liveness, NormalAccess posture, legacy-endpoint pragma).

## Decisions

- Eligibility uses current account sets (`EffectiveAccountTypes`) and
  relationship state; the first-registered display integer is display-only.
  Non-active (suspended/blocked) users resolve as unknown.
- Reopen (existing visible/hidden) checks membership + recipient liveness;
  creation checks the matrix; terminal bookings reopen but never create.
- Replay is sender-bound (other sender + any content = 409).
- Read marks validate same-conversation existence (privacy 404), advance
  monotonically in (sent, id) total order, stamp stable first-reads, and
  emit receipt changes only on transitions.
- Delivery (history + sync) stamps set-once with durable receipt changes in
  the same commit; revision-token collisions retry bounded (3) with
  duplicate-safe re-staging; identified unique violations translate to
  replay/409/400, everything else propagates.
- Conversation list: activity desc + in-memory id tie-break, batched
  Identity/facility enrichment (one round-trip each per page); unread-count
  scans visible conversations in bounded 200-pages with exact totals.
- Support array endpoint kept with >1,000-message 409
  (`Chat.Report.HistoryRequiresPagination`, deprecated); new paged endpoint
  returns `MessageCursorPage`.
- Limits: `ChatPlus:RateLimits` (contracts.md defaults), sliding windows,
  429 + Retry-After; uploads additionally gated by 2 concurrent slots
  (controller, before buffering) and 100 MiB live-unbound quota with 24 h
  expiry (expired unbound binds read as 404; bound history never expires).
- Chat module keeps `DateTime.UtcNow` (module precedent; single-instance
  wall-clock); new cross-module/pricing code uses `IDateTimeProvider`.
  Coordinates reject NaN/Infinity.

## Verification (executed)

- `dotnet build` Chat.Domain / Chat.Application / Chat.Infrastructure /
  Sanad.API / UnitTests: 0 warnings, 0 errors (each).
- New: 22/22 ChatPlus facts pass (open/reopen, replay, expiry, reads,
  reports, list order, privacy, rate boundaries, sync incl. >100-message
  recovery, receipt-only deltas, cursor auth, reset, ack scope).
- Regression: full `Sanad.UnitTests.Chat*` filter 179/179 pass (includes
  the 22 new; 16 pre-existing failures found during the turn were
  investigated — all pinned superseded behavior — and updated, none
  weakened).
- Migration `AddChatPlusChanges` generated via `dotnet ef` (reviewed:
  3 tables, 2 columns, 8 indexes); `has-pending-model-changes` clean
  implied by successful generation (snapshot updated).

## Skipped / blocked (Phase 7)

- PostgreSQL provider proof (unique-index races, token collisions,
  Npgsql ordering/translation, repeatable-read snapshot): no exact-target
  authorization this turn; InMemory does not enforce unique indexes.
- Stateful API startup, fixture writes, provider sends, deployment:
  not authorized; not attempted.
- Voice: excluded, untouched (`MessageType` unchanged, audio still
  `UnsupportedType`).
