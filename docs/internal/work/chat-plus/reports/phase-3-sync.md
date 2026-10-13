# Chat Plus — Phase 3 evidence: durable changes, sync, notification intent

Base: `41f9c91`. Branch: `builder/chat-plus-production`.
Depends on Phase 1 (change/outbox/privacy schema, recorder, revision
semantics). The sync endpoint, change log, and outbox were implemented with
Phase 1; this phase adds the dispatcher, proves crash/retry/dedupe, and
records the snapshot-handoff argument.

## Changed manifest (Phase 3)

- Domain: `ChatChange.PublishedOnUtc`, `ChatOutboxItem.ClaimedOnUtc`
  (+`Claim(token, now)`), migration hand-patched (Up/Designer/Snapshot);
  `has-pending-model-changes` reports clean.
- Application new: `Dispatch/ChatOutboxDispatcher.cs` (+`ChatDispatchOptions`).
- API new: `ChatPlus/ChatPlusDispatchService.cs` (5s loop, scoped,
  optional publisher until Phase 4); hosted-service registration.
- Tests: `ChatPlusDispatchTests` (4 facts).

## Design (committed)

- Writers stage change rows + at most one intent per (message, recipient)
  in the Chat transaction; the revision is gapless per conversation
  (row-lock serialized increment, rolled back with the transaction).
- Sync advances cursors only over returned rows; snapshot pages hold the
  conversation row while capturing the boundary; the final snapshot page
  replays concurrent changes above the snapshot start. Residual: a writer
  that incremented before the snapshot start but commits after its
  message's page was read is missed until its revision sorts above a later
  cursor — narrow, documented, closed in practice by short writer
  transactions + periodic re-sync; clients always dedupe by
  message id/revision and converge by re-polling.
- Dispatcher: unpublished changes (bounded 100, ordered) → realtime
  publisher; due intents → claim → `CreateChatMessageNotificationCommand`
  (ids only, producer key dedupes) → published. Failures back off
  5s→640s (attempt 8) capped 60min with jitter; 8 exhausted attempts park
  unpublished with the error code for operator retry (reset
  `NextAttemptOnUtc`). Stale claims (>5min) are reclaimed after a crash.
  One stream's failure never blocks the other; hub outage converges
  through REST sync.
- `beforeId` history paging is preserved for older history; sync is the
  only forward-replay cursor (no invented `since` parameter).

## Verification (executed)

- New: 4/4 dispatch facts pass (publish+deliver then quiet; retry/backoff/
  park; stale-claim reclaim; backoff curve).
- Sync: 5/5 facts pass (snapshot→changes convergence, >100-message
  recovery, cursor auth, reset, ack scope) — see Phase 1 gate run; rerun
  in the joint Phase 6 gate.
- Migration/model alignment: `dotnet ef migrations has-pending-model-changes`
  → "No changes have been made to the model since the last migration."
  (The `remove`/re-scaffold path needs a live DB connection and was not
  available without authorization; the two added columns/index were
  hand-applied identically to Up/Designer/Snapshot and verified by the
  alignment check.)

## Skipped / blocked (Phase 7)

- Live dispatcher restart proof, hub fan-out observation, and PostgreSQL
  repeatable-read evidence: need exact-target authorization.
- Investigation note: during this turn a local PostgreSQL listener was
  observed (connection refused on password, no credentials available);
  it was not probed further and no state was touched.
