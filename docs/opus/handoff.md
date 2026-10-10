# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state — package complete (session 2, model `opencode/mimo-v2.6-flash-free`)

Branch `opencode/opus-work` (base `main` @ `fe91d64`), committed per phase,
reports in `docs/opus/reports/`. Ready boxes checked for 1, 2, 3, 5, 6;
phase 7 box checked in the Order list (gates report:
`phase-7-gates.md`; its content commit `3b06c46`, tip carried by the docs
commits after it).

- Phase 1: rating unification — `e8d9e6f`.
- Phase 2: community categories + migration `20261010004134_AddCommunityCategories`
  — `b35a040`.
- Phase 3: feed search/filter/sort, favorites, per-user state, `categoryId`
  — `9c873ec`.
- Phase 4: **Blocked** — `src/Modules/Chat` still absent from git and the
  working tree. Re-verify with `git ls-files src/Modules/Chat`; skipped per
  the Ready-box rule, never force-implemented.
- Phase 5: library in CMS + migration `20261010131314_AddLibraryContent` —
  `8676b6a` (+ docs `8614a81`, `8adccf3`). Owner flags: nine `Cms.Library*`
  mapper lines; `Cms.Application → Families.Application/Domain` reference
  (benefit key 8).
- Phase 6: caregiver payout reads + bilingual policy descriptions +
  migration `20261010143327_AddCaregiverPayoutPolicyDescriptions` —
  `1f9e0aa` (+ docs `565c87d`). Owner flags: no mapper line
  (`Finance.PayoutPolicy.Missing` keeps the existing 400 default, same as
  admin record); policy create gained optional descriptions
  (contract-mandated, additive); a pre-existing date-dependent
  `PlatformChargeRuleTests` time-bomb was repaired (test-only).
- Phase 7: doc/Postman sync + gates — content `3b06c46`. Full gates:
  build 0 warnings/0 errors; full unit 2691 passed / 0 failed /
  3 pre-existing static skips; full architecture 1/1; `git diff --check`
  clean; all 20 Postman JSONs valid.

Nothing pushed, nothing merged, no migrations applied, no seeds, no
provider calls.

## Blockers

- **Phase 4 blocked pending dependency:** the Chat lane's core
  implementation must land on `main` (or be supplied) before chat deltas
  can be implemented.

## Next action

Integration only, on owner trigger "integrate now": the Integrator scans
the Ready boxes in `tasks.md`, merges the qualifying phases (1, 2, 3, 5,
6 — phase 4 stays blocked/skipped) in numeric order, marks each merged box
with the `main` SHA, and pushes `main` once. Workers never merge or push.
Dated history, if any accumulates, is archived under `docs/opus/history/`
— never stacked here.
