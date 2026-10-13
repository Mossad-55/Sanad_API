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

## Current state — 2026-10-13 (builder, end of day)

Branch `builder/chat-plus-production` (from `41f9c91`) holds the
implementation. Phases 0, 1, 3 Done with evidence reports; Phase 2
code-complete (pricing guide/Postman pending, may ride Phase 6); Phases
4–6 not started; Phase 7 blocked on exact-target authorizations. All
migrations authored-only, never applied. Voice excluded. No API/DB/
provider/production operation performed — unit gates only (InMemory).
See `tasks.md` for the phase checklist and `reports/` for evidence.

## Next action

Continue on `builder/chat-plus-production`: finish Phase 2 docs,
then implement Phases 4 → 5 → 6 in order; do not merge, push main,
or deploy — hand the branch to the planning mastermind for review
and integration.
