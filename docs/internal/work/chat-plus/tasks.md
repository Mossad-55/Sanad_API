# Chat Plus — implementation checklist, revision 2

Single status/evidence checklist for [plan.md](plan.md). Updated 2026-10-13
from [frontend input](frontend-report.md). Owner implements/commits/pushes;
mastermind prepared this package and will review the resulting SHA.
No production code or live verification was performed in this planning turn.

Phase 0 uses the concrete proposed defaults and wire shapes in
[contracts.md](contracts.md); changes require synchronized plan/test updates.

Statuses: Not started, In progress, Blocked, Done. Implementation readiness
and release readiness are distinct; do not mark external gates complete from
unit evidence. Voice remains excluded.

- [x] Done — Planning revision: capture frontend report, correct authentication/
  replay/scale assumptions, add prerequisite hardening and external release gates.
- [x] Done — **Phase 0: execution contract and scout.**
  Base `41f9c91` pinned; branch `builder/chat-plus-production` owns the work.
  Report: `reports/phase-0-contract.md` (manifest, booking paths, migration
  order, DTOs, limits, verification, Phase 7 gates).
  Original scope (retained): confirm current main SHA/clean tree and preserve
  unrelated edits; accept/adjust plan section 11 with owner; pin exact manifest,
  actor matrix, area/service attribution write path and sample formula,
  snapshot/change DTOs, preference backward compatibility, privacy API,
  clock/revision ordering, numeric rate/quota limits, unbound-upload policy,
  migration sequence, and runnable client harness; pin live fixtures/actors
  and exact authorization separately; block only dependent tasks if a choice
  is unresolved.
- [x] Done — **Phase 1: REST correctness, privacy, and resource bounds.**
  Implemented + focused-tested (report `reports/phase-1-hardening.md`).
  Pre-existing Chat tests pinning superseded behavior were updated;
  none weakened.
  Owns approved Chat application/domain/configuration/controller changes,
  eligibility/booking/Identity gateways, private-upload path, focused tests.
  Implement actor matrix, Normal access, retained-history reopening, concurrent
  open/send/replay sender check, atomic single attachment bind, monotonic reads/
  activity, stable receipts, concurrent report resolution, SQL unread/latest,
  batch profiles, Support history pagination, upload/resource controls.
  Author reviewed additive migrations; never apply automatically for a gate.
- [x] Done — **Phase 2: pricing insights.**
  Code + focused tests (9 facts) green; frontend guide + Postman examples
  verified in the Phase 6 pass (report `reports/phase-6-gates.md` §2).
  Live PostgreSQL aggregate equality stays a Phase 7 gate.
  Depends on accepted attributable booking data contract from Phase 0.
  Owns scoped caregiver/booking gateway and immutable attribution addition,
  query/route/DTO, aggregate/provider tests, frontend guide and Postman.
  Preserve exact frontend response and independent null-range semantics.
  Validate distinct-booking pools and same-data direct PostgreSQL aggregates.
- [x] Done — **Phase 3: durable changes, sync, and notification intent.**
  Change/outbox persistence, sync endpoint, dispatcher + hosted service,
  crash/retry/dedupe proofs (report `reports/phase-3-sync.md`). Hub
  fan-out implementation belongs to Phase 4; the publisher seam is ready.
  Depends on Phase 1. Owns Chat transactional change/outbox persistence,
  commit-safe cursor/revisions, sync controller/query/DTO, dispatcher seam,
  crash/retry/dedupe/provider tests and sync guide. Prove snapshot handoff and
  >100-message plus receipt-only catch-up; preserve beforeId for older history.
- [x] Done — **Phase 4: hub, typing, presence, and client recovery.**
  Implemented + focused-tested incl. integrated reconnect recovery
  (report `reports/phase-4-realtime.md`). One sync payload defect found
  and fixed in-turn with a locking test.
  Depends on Phases 1/3 and accepted privacy contract.
  Owns thin hub, supported JWT transport config, validated sub routing,
  authorization eviction/dispatch, opt-in privacy settings, registries,
  expiry/rate controls, single-instance topology validation, client harness
  and realtime guide. Prove token/logout/block/hide/role behavior, multi-device
  OR, restart, fallback and event/polling convergence. No assumed Redis support.
- [x] Done — **Phase 5: chat push integration and operating instructions.**
  Code + focused tests (report `reports/phase-5-push.md`); operating
  instructions + secret-free Firebase/APNs checklist delivered as
  `docs/app/chat/operations.md` in the Phase 6 pass. Live delivery stays a
  Phase 7 gate.
  Depends on durable intent and accepted preference compatibility.
  Owns existing Chat producer/PushFanoutPolicy/outbox processor/provider
  classification corrections, preference compatibility, fake-provider tests,
  ids-only navigation payloads, secret-free Firebase/APNs checklist.
  Prove an actual Chat/NewMessage producer enqueues eligible push and survives
  crash/retries. Android/iOS external setup remains owner-operated.
- [x] Done — **Phase 6: documentation, review, joint gates.**
  App/admin guides synced to the implementation (Normal access, enforced
  matrix + retained-history reopen, monotonic reads, durable outbox,
  paged history + 409 compat, preference compat, attribution, mapping);
  CloseOnAuthenticationExpiration and orphan-cleanup claims corrected.
  One bounded independent review: 6 concrete findings, all resolved with
  locking facts. Gates at `896c46b`: solution build 0 warnings/0 errors;
  full unit 3070 passed / 0 failed / 3 PG-only skips; architecture 1/1;
  mapping 555 signatures with 0 missing / 0 orphan Postman routes (Bruno
  risk-based, 0 orphans); Postman parse 22/22; diff clean; EF alignment
  clean (Chat/Families/Identity). Report: `reports/phase-6-gates.md`.
- [ ] Not started — **Phase 7: release verification (separate authorization).**
  Exact disposable target migration/fixture/runtime checks and two-client socket
  tests incl. socket-kill/REST fallback, expiry and privacy. Verify deployed
  proxy/origins/redaction/resource limits and migration rollback compatibility.
  Authorized Android/physical iPhone Firebase/APNs proof including taps/token
  refresh/logout/preferences/block. Sessions cleaned, API stopped; no broad
  DB reset/manual cleanup. Deferred/unauthorized provider proof remains open.

## Evidence per phase

Write reports/phase-N-<slug>.md with base/final SHA, accepted contracts, exact
changed manifest, checks actually run, warnings/skips, authored/applied migration
distinction, findings resolved, client ownership and remaining release gates.
Reports describe evidence, not substitutes for unchecked acceptance criteria.

Reports expected after implementation:
- reports/phase-0-contract.md
- reports/phase-1-hardening.md
- reports/phase-2-pricing.md
- reports/phase-3-sync.md
- reports/phase-4-realtime.md
- reports/phase-5-push.md
- reports/phase-6-gates.md
- reports/phase-7-release.md

## Final review

Owner supplies pushed commit SHA and implementation reports. Mastermind reviews
that exact revision against plan/frontend contracts and accepted Phase 0 choices.
Do not reopen unrelated Care Homes/UI slices, overwrite owner edits, claim
unrun gates passed, or call deployment production-ready while Phase 7 is open.
