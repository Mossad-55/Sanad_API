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

## Next action

Phase 6 on this branch: operating instructions + Firebase/APNs
checklist, guide/Postman-to-implementation verification pass, one
bounded independent review with findings resolved, full solution
build + unit/architecture gates + contract mapping + Postman parse +
diff check, final report and handoff. Then mastermind review and
integration. Do not merge, push main, or deploy.
