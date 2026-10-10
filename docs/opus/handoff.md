# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state

Phase 0 done (branch `opencode/opus-work` from `main` @ `fe91d64`, clean
base). Phase 1 (rating unification) done — content commit `e8d9e6f`, report
`docs/opus/reports/phase-1-rating.md`, Ready box checked. Phase 2 (community
categories) is next. Effective model: `opencode/mimo-v2.6-flash-free`.

## Blockers

None. Shared contracts unchanged so far (unmapped error codes already default
to 400). Dated history, if any accumulates, is archived under
`docs/opus/history/` — never stacked here.

## Next action

Opus continues with Phase 2 (community categories + additive migration,
authored only — never applied).
