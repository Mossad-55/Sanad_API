# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state — package complete (model `opencode/muse-spark-1.3-contributor-free`)

Branch `opencode/opus-work` (base `main` @ `fe91d64`), committed per phase,
reports in `docs/opus/reports/`. Ready boxes checked for 1, 2, 3, 4, 5, 6;
phase 7 box checked in the Order list (gates report:
`phase-7-gates.md`).

- Phase 1: rating unification — `e8d9e6f`.
- Phase 2: community categories + migration `20261010004134_AddCommunityCategories`
  — `b35a040`.
- Phase 3: feed search/filter/sort, favorites, per-user state, `categoryId`
  — `9c873ec`.
- Phase 4: chat deltas — content `a9d8819` on top of the `21081a8` merge of
  `origin/main` @ `9c2e9ca` (Chat Lane A+B core; the documented unblock
  condition, re-verified). All 7 deltas: open XOR with backend booking
  resolution, `beforeId`/`pageSize` + `conversationId` + wire `createdOnUtc`,
  `Location` type + additive migration `20261011145248_AddChatMessageLocation`
  (authored only), read POST, attachment route, `accountType` + facility
  display + `contactUserId`, polling verified. 23 new tests; full unit
  2903/0/3, architecture 1/1. Report: `phase-4-chat-deltas.md` (flags:
  wire-only createdOnUtc, literal terminal sets, first-registered
  accountType, remaining role-matrix gap, merge resolution of the Finance
  test fix).
- Phase 5: library in CMS + migration `20261010131314_AddLibraryContent` —
  `8676b6a` (+ docs `8614a81`, `8adccf3`). Owner flags: nine `Cms.Library*`
  mapper lines; `Cms.Application → Families.Application/Domain` reference
  (benefit key 8).
- Phase 6: caregiver payout reads + bilingual policy descriptions +
  migration `20261010143327_AddCaregiverPayoutPolicyDescriptions` —
  `1f9e0aa` (+ docs `565c87d`). Owner flags: no mapper line
  (`Finance.PayoutPolicy.Missing` keeps the existing 400 default, same as
  admin record); policy create gained optional descriptions
  (contract-mandated, additive). Note: the phase-6 `PlatformChargeRuleTests`
  repair was superseded by main's clock-relative fix during the `21081a8`
  merge.
- Phase 7: doc/Postman sync + gates — content `3b06c46`. Full gates:
  build 0 warnings/0 errors; full unit 2691 passed / 0 failed /
  3 pre-existing static skips; full architecture 1/1; `git diff --check`
  clean; all 20 Postman JSONs valid.

Nothing pushed, nothing merged, no migrations applied, no seeds, no
provider calls.

## Blockers

None remaining. All seven phases have checked Ready boxes and reports.

## Next action

Integration only, on owner trigger "integrate now": the Integrator scans
the Ready boxes in `tasks.md`, merges the qualifying phases (1–6 — phase 4
depends on the chat core already in `main`) in numeric order, marks each
merged box with the `main` SHA, and pushes `main` once. Workers never merge
or push. Dated history, if any accumulates, is archived under
`docs/opus/history/` — never stacked here.
