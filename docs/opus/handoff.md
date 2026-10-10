# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state

Session 1 (model `opencode/mimo-v2.6-flash-free`) completed Phases 0–3 on
`opencode/opus-work` (base `main` @ `fe91d64`), committed per phase, reports
in `docs/opus/reports/`, Ready boxes checked:

- Phase 0: branch created, clean base.
- Phase 1: rating unification — content `e8d9e6f`, report `phase-1-rating.md`.
- Phase 2: community categories + authored-only migration
  `20261010004134_AddCommunityCategories` — content `b35a040`, report
  `phase-2-categories.md`. Note: five Community status mappings were added to
  shared `ResultProblemDetailsMapper.cs` (required by the frozen 404/409
  contract) — flagged in the report.
- Phase 3: feed search/filter/sort, favorites list, per-user state, post
  `categoryId` — content `9c873ec`, report `phase-3-search.md`.
- Branch tip after report commits: `7f38fdd` (last commit: phase-3 report +
  Ready box). Working tree clean. Nothing pushed, nothing merged.

Phase 4 (chat deltas) exploration started and STOPPED by owner request.
Finding for the next session: `src/Modules/Chat` is **not tracked in git on
any branch of this clone and does not exist in the working tree**. Chat
implementation sources were briefly readable from untracked local files at
the start of session 1 (OpenConversation handler, entities, controller,
etc.) and have since disappeared; only `docs/chat/{plan,handoff,tasks}.md`
are tracked. Phase 4 is a *delta* over the Chat lane's implementation, so
its dependency (chat core code on `main`) is currently missing.

## Blockers

- **Phase 4 blocked pending dependency:** the Chat lane's core
  implementation must land on `main` (or be supplied) before chat deltas can
  be implemented. Per the Ready-box rule a phase whose dependencies are not
  in main is skipped as blocked, never force-merged. Next session: re-verify
  (`git ls-files src/Modules/Chat`); if still absent, mark Phase 4 Blocked
  in `docs/opus/tasks.md` with a short note, skip it, and continue with
  independent phases 5–6–7 (no cross-dependencies on chat).

## Next action

New session continues at Phase 4 verification, then Phase 5 (library in CMS
+ authored-only migration) and Phase 6 (caregiver payout reads), then
Phase 7 (docs/Postman sync + full build 0 warnings + full unit and
architecture suites + `git diff --check` + Postman JSON validation). Reports
required for every completed phase before integration. Dated history, if
any accumulates, is archived under `docs/opus/history/` — never stacked
here.
