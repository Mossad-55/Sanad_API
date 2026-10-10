# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state

Branch `opencode/opus-work` (base `main` @ `fe91d64`), committed per phase,
reports in `docs/opus/reports/`, Ready boxes checked for 1, 2, 3, 5, 6:

- Phase 1: rating unification — `e8d9e6f`, report `phase-1-rating.md`.
- Phase 2: community categories + authored-only migration
  `20261010004134_AddCommunityCategories` — `b35a040`,
  report `phase-2-categories.md`.
- Phase 3: feed search/filter/sort, favorites, per-user state, post
  `categoryId` — `9c873ec`, report `phase-3-search.md`.
- Phase 4: **Blocked** — `src/Modules/Chat` still absent from git and the
  working tree (re-verified session 2). Re-verify with
  `git ls-files src/Modules/Chat`; never force-implement chat core.
- Phase 5 (session 2): library in CMS + authored-only migration
  `20261010131314_AddLibraryContent` — content `8676b6a`, report +
  boxes `8614a81` (+`8adccf3` whitespace fix). The tree had arrived with
  uncommitted in-progress Phase 5 source; audited, completed, committed.
  Owner flags: nine `Cms.Library*` mapper lines; one-directional
  `Cms.Application → Families.Application/Domain` reference (benefit key 8).
- Phase 6 (session 2): caregiver payout reads (summary/list/detail/
  payment-policy under `CaregiverAccess`) + bilingual policy descriptions
  with additive-only migration `20261010143327_AddCaregiverPayoutPolicyDescriptions`
  — content `1f9e0aa`, report `phase-6-payout-reads.md`. `Transferred ≡ Paid`
  documented, no Processing bucket, honest nulls, `net == gross` tested.
  Owner flags in the report: no mapper line added (`Finance.PayoutPolicy.Missing`
  keeps the existing 400 default, same as admin record); policy create
  gained optional descriptions (contract-mandated, additive);
  `docs/admin/finance-platform-charges.md` create-body example is stale
  until Phase 7; a pre-existing date-dependent PlatformChargeRule test
  time-bomb was repaired (test-only).

Session 2 model: `opencode/mimo-v2.6-flash-free`. Nothing pushed, merged,
or deployed.

## Blockers

- **Phase 4 blocked pending dependency:** the Chat lane's core
  implementation must land on `main` (or be supplied) before chat deltas
  can be implemented.

## Next action

Phase 7 only: docs + Postman sync (including the stale
`docs/admin/finance-platform-charges.md` create-body example and any other
doc drift found), then the gates — full solution build with `-warnaserror`,
full unit + architecture suites **once**, `git diff --check`, Postman JSON
validation — plus its report, Ready box, and handoff. Dated history, if
any accumulates, is archived under `docs/opus/history/` — never stacked
here.
