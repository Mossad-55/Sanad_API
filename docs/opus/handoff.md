# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state
Planned and approved. No implementation has started. Contract, phases, and
file manifests are frozen in `plan.md` / `tasks.md`.

## Blockers
None. Opus starts with Phase 0 on a task branch from latest `main` once the
owner assigns it. Dated history, if any accumulates, is archived under
`docs/opus/history/` — never stacked here.

## Next action
Owner assigns the package to Opus; Opus confirms the base SHA and begins
Phase 0. Shared contracts (DTOs, error codes) change only through the owner.
