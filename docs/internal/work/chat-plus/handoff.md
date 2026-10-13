# Chat Plus — execution handoff, revision 2

## Goal

Owner implements the production package in [plan.md](plan.md), tracked solely
in [tasks.md](tasks.md), with the [frontend report](frontend-report.md) retained
as input. Mastermind reviews the owner's final pushed revision.

[contracts.md](contracts.md) supplies concrete proposed additive APIs, numeric
limits, pricing attribution and compatibility rules for Phase 0 acceptance.

## Current state — 2026-10-13

Planning revision prepared against clean main `41f9c91`. No Chat Plus hub,
pricing-insights query, presence, or recovery implementation was found.
Existing REST Chat is implemented but the review found missing actor eligibility,
concurrency/read/attachment weaknesses, post-commit notification gaps, unbounded
read queries, and Chat/NewMessage excluded by push fan-out.

Owner explicitly requested plan/handoff updates and will implement, commit, and
push. This turn authored documentation only. Voice remains excluded because
the frontend request is conditional and the owner previously dropped it.
Firebase/APNs configuration preparation belongs to the package; provider/device
proof stays an outstanding release gate until specifically authorized.

## Decisions and dependencies

The old blocker-free claim is withdrawn. Plan section 11 lists concrete NEW
proposed business rules for owner acceptance in Phase 0. Pricing depends on a
reliable immutable booked area/service write path; current Booking lacks it.
Presence requires opt-in privacy semantics; current notification preferences
do not supply these. Frontend client recovery must have an owner/deliverable.

Transport corrections: standard authenticated SignalR, scoped browser query
token handling/redaction, commit-safe REST sync for newer messages AND receipts,
explicit authorization eviction and single-instance limits. Reviewed additive
schema changes are permitted to be authored; no migration application, seed,
provider send, runtime startup, or deployment is authorized by planning.

## Current state — 2026-10-13 (mastermind, stable point)

Branch `builder/chat-plus-production` (from `41f9c91`). Phases 0, 1, 3,
4 Done with evidence reports; Phase 2 code-complete (guide + Postman
authored by documenter — verify in Phase 6 review); Phase 5 code-complete
with tests (operating instructions + Firebase/APNs checklist pending);
Phase 6 not started; Phase 7 blocked on exact-target authorizations.
All migrations authored-only, never applied. Voice excluded. No API/DB/
provider/production operation performed — unit gates only (InMemory).
`ChatPlus` filter 52/52 green; affected suites (Chat incl. ChatPlus,
Notifications, Identity account/users, Families, Caregivers) green;
builds zero warnings/errors. Working tree committed; branch unpushed —
mastermind owns review, integration into main, and pushing main.
See `tasks.md` for the checklist and `reports/` for evidence.

## Current state — 2026-10-13 (Phase 6 closeout)

Branch `builder/chat-plus-production` (from `41f9c91`). Phases 0–6 Done
with evidence reports (`reports/phase-0-contract.md` through
`reports/phase-6-gates.md`); Phase 7 blocked on exact-target
authorizations. Phase 6 commit `896c46b`: operating instructions +
secret-free Firebase/APNs checklist (`docs/app/chat/operations.md`),
guide/Postman-to-implementation verification with stale-path repairs, one
bounded independent review (6 concrete findings, all resolved with locking
facts — ChatPlus 58/58). Joint gates at `896c46b`: solution build 0
warnings/0 errors; full unit 3070 passed / 0 failed / 3 PostgreSQL-only
skips; architecture 1/1; mapping 555 signatures with 0 missing / 0 orphan
Postman routes (Bruno risk-based, 0 orphans); Postman parse 22/22; diff
clean; EF alignment clean (Chat/Families/Identity). Migrations
authored-only, never applied: Chat `20261013000659_AddChatPlusChanges`,
Families `20261013005844_AddBookingAreaServiceAttribution`, Identity
`20261013104759_AddChatMessagesPreference`. Voice excluded. No API/DB/
provider/production operation performed — unit gates only (InMemory).
Working tree committed; branch unpushed — mastermind owns review,
integration into main, and pushing main. This closeout (report +
checklist/handoff sync) follows as the final commit; its SHA is recorded
below. See `tasks.md` for the checklist and `reports/` for evidence.

Final Phase 6 commits: `896c46b` (operations guide, doc verification,
review resolutions, locking facts — all gates in §4 run at this revision)
plus the closeout commit carrying this handoff and `reports/phase-6-gates.md`
(tip of `builder/chat-plus-production`; base `41f9c91`).

## Current state — 2026-10-13 (Phase 7 backend gates)

Authorizations consumed exactly as granted (disposable DB, :5235 Dev run +
stop, apply + rollback, minimal fixtures, owner-run push proof). Report:
`reports/phase-7-release.md`. 3/3 migrations applied live and rollback-
proven; the live apply caught and fixed one real defect (Identity backfill
referenced never-existing columns — `has-pending-model-changes` is blind
to hand-tuned SQL drift). Runtime manifest 47/47 green on the disposable
target; sessions logged out, API stopped, port free. Re-greened gates on
the final tree: build 0/0, unit 3070/0/3-skips, arch 1/1, diff clean.
Phase 7 is Blocked solely on the owner-run Android + physical iPhone
delivery proof (plus the authorized-payment-fixture satisfied-pool
equality). No production readiness claimed.

## Next action

Mastermind review of the final pushed revision, then integration into
main. Phase 7 needs separate exact-target authorizations (migration/
runtime, proxy/TLS/origins, Android + physical iPhone delivery proof).
Do not merge, push main, or deploy from this handoff. Never claim
production readiness while Phase 7 is open.
