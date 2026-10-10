# Opus work package — execution handoff

## Goal
Deliver the owner-approved Opus scope (library, payout reads, community
additions, rating unification, chat deltas) per `plan.md`, phases 0–7 in
`tasks.md`. Chat core lanes stay with the Chat lane owner; Opus touches chat
files only for the listed deltas.

## Current state

Branch `opencode/opus-work` (base `main` @ `fe91d64`), committed per phase,
reports in `docs/opus/reports/`, Ready boxes checked for 1, 2, 3, 5:

- Phase 1: rating unification — `e8d9e6f`, report `phase-1-rating.md`.
- Phase 2: community categories + authored-only migration
  `20261010004134_AddCommunityCategories` — `b35a040`,
  report `phase-2-categories.md`. Five Community status mappings added to
  shared `ResultProblemDetailsMapper.cs` (flagged).
- Phase 3: feed search/filter/sort, favorites, per-user state, post
  `categoryId` — `9c873ec`, report `phase-3-search.md`.
- Phase 4: **Blocked** — `src/Modules/Chat` still absent from git and the
  working tree (re-verified in session 2). Chat core must land on `main`
  first; skipped per the Ready-box rule, never force-implemented.
- Phase 5 (session 2, model `opencode/mimo-v2.6-flash-free`): library in
  CMS + authored-only migration `20261010131314_AddLibraryContent` —
  content `8676b6a`, report `phase-5-library.md`. The tree arrived with
  uncommitted in-progress Phase 5 source (not mentioned in the session-1
  handoff); it was audited, completed (tests/docs/Postman), and committed.
  Owner flags in the report: nine `Cms.Library*` mapper lines, and a new
  one-directional `Cms.Application → Families.Application/Domain`
  reference for the frozen benefit-key-8 premium gate.

Nothing pushed, nothing merged.

## Blockers

- **Phase 4 blocked pending dependency:** the Chat lane's core
  implementation must land on `main` (or be supplied) before chat deltas can
  be implemented. Re-verify with `git ls-files src/Modules/Chat` at the next
  session start.

## Next action

Continue with Phase 6 (caregiver payout reads: summary/list/detail/
payment-policy; Transferred≡Paid documented, Processing bucket removed,
honest nulls; no ledger/transfer/policy-admin changes), then Phase 7
(docs/Postman sync + full build 0 warnings + full unit and architecture
suites once + `git diff --check` + Postman JSON validation). Reports
required for every completed phase before integration. Dated history, if
any accumulates, is archived under `docs/opus/history/` — never stacked
here.
