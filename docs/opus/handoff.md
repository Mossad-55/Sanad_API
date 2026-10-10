# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state

Phases 0–3 done on `opencode/opus-work` (base `main` @ `fe91d64`):
phase-1 `e8d9e6f`, phase-2 `b35a040`, phase-3 `9c873ec`; reports under
`docs/opus/reports/`, Ready boxes checked. Phase 2 added five Community
status mappings to shared `ResultProblemDetailsMapper.cs` (required by the
frozen 404/409 contract) — flagged in the phase-2 report. Effective model:
`opencode/mimo-v2.6-flash-free`.

## Blockers

None. Dated history, if any accumulates, is archived under
`docs/opus/history/` — never stacked here.

## Next action

Opus continues with Phase 4 (chat deltas per locked plan §2 + frozen
`docs/chat/plan.md` rules: booking-open resolution, Location type, POST read,
attachment route, facility `contactUserId`, accountType/care-home resolution).
