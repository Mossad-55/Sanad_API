# Notifications slice — execution handoff

## Goal
Deliver the owner-approved backend Notifications slice per `plan.md`, Lane A (Push, Codex) and Lane B (email overhaul, assignee TBD).

## Current state
Planned and approved. No implementation has started. Contract, lanes, and
file manifests are frozen in `plan.md` / `tasks.md`.

## Blockers
None. Lane A (Codex) and Lane B can start on task branches once the owner
confirms lane assignment. Firebase service-account key placement
(`/etc/sanad/sanad.env`) is needed at implementation/verification time, not
for planning.

## Next action
Owner confirms who builds Lane B (and whether Codex takes Lane A as discussed); then Lane A begins on a task branch. Dated history, if any accumulates, is archived under `docs/notifications/history/` — never stacked here.
