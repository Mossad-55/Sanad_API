# Opus work package — task checklist

Single implementation checklist for the Opus scope (library, payout reads,
community additions, rating unification). Chat is excluded. Statuses: Not
started, In progress, Blocked, Done. The contract in `plan.md` is frozen;
changing it needs a new explicit owner decision. File manifests are
exclusive: Opus edits only the files below plus the listed docs/collections.

## Order (dependency-ordered, commit per phase locally, no push/merge/deploy)

- [x] Done — **Phase 0.** Branch `opencode/opus-work` from latest `main`; verify clean base.
  Owns: new branch only.

- [x] Done — **Phase 1. Rating unification.**
  Owns: `Modules/Community/Application/Ratings/AddRatingCommand.cs` (+handler
  range validation), `Sanad.API/Controllers/CommunityPostsController.cs`
  (binding only), `tests/.../Community/*Rating*`, Postman community requests,
  `docs/app/community/writes.md` touch-up.

- [x] Done — **Phase 2. Community categories (+migration).**
  Owns: `Modules/Community/Domain` (category entity),
  `Modules/Community/Application` (category lookups + admin CRUD),
  `Modules/Community/Infrastructure` (config + additive migration authored
  only, DI untouched), `Sanad.API/Controllers` (admin category endpoints +
  public list), tests, docs, Postman.

- [x] Done — **Phase 3. Community search/filter/sort + favorites + per-user state.**
  Owns: `GetPostsQuery` extension, new favorites-list query,
  `CommunityPostResponse` additive fields, `CreatePostCommand` (+`categoryId`),
  controller query params, tests, docs, Postman. Moderation untouched.

- [x] Done — **Phase 4. Chat deltas.** Unblocked this session by merging
  `origin/main` @ `9c2e9ca` (Chat Lane A+B core) into the work branch
  (`21081a8`); chat core re-verified (71 files). Applied all 7 deltas:
  open-by-`bookingId` XOR `recipientUserId` with backend counterpart
  resolution + validity (new `IChatBookingGateway` port, `400
  Chat.Conversation.OpenTargetInvalid` on both/neither, privacy 404 on all
  denials); `beforeId`/`pageSize` + `conversationId` + wire `createdOnUtc`;
  `Location` type + payload + additive migration
  `20261011145248_AddChatMessageLocation` (authored only); read marking
  PUT→POST; attachment download at `GET /api/v1/chat/attachments/{id}/file`
  with attachment-derived participant check; `accountType` + care-home
  facility display + additive `contactUserId` on facility detail (authed
  callers only); realtime verified polling-only (no changes).
  Owns: `Modules/Chat/*` deltas (booking-open resolution, Location type +
  validation, POST read, attachment route + `contactUserId` on facility
  detail, accountType/care-home resolution), `Sanad.API/Controllers/Chat*`,
  tests, `docs/chat/*` deltas, Postman chat deltas. Frozen chat rules
  (1:1, send-only, polling, no groups) are not renegotiated here.
  Gates: build 0W/0E; full unit 2903 passed / 0 failed / 3 pre-existing
  static skips; architecture 1/1; all 20 Postman JSONs valid. 23 new tests
  (incl. gateway tests on real InMemory Families/Caregivers/CareHomes
  stores). See report for interpretation flags (wire-only createdOnUtc,
  literal terminal-status sets, first-registered accountType, remaining
  role-matrix gap).

- [x] Done — **Phase 5. Library in CMS (+migration).**
  Owns: `Modules/Cms/{Domain,Application,Infrastructure}` (categories,
  content, sections, saved/liked/views), `Sanad.API/Controllers`
  (public + `CmsContent` admin endpoints), cover upload handling, tests,
  new `docs/app/library/` guide, Postman App library folder. Additive
  migration `20261010131314_AddLibraryContent` authored only (never
  applied). Note: shared `ResultProblemDetailsMapper.cs` gained nine
  `Cms.Library*` status lines and `Cms.Application` references
  `Families.Application`/`Families.Domain` for the frozen benefit-key-8
  premium gate — both flagged in the report.

- [x] Done — **Phase 6. Caregiver payout reads.**
  Owns: `Modules/Caregivers/Application` (summary/list/detail/policy-read
  queries), `Modules/Finance` (policy description fields + additive
  migration `20261010143327_AddCaregiverPayoutPolicyDescriptions`, authored
  only), `Sanad.API/Controllers` (caregiver payout reads), tests,
  `payouts.md` deltas, Postman deltas. No ledger/transfer changes; the
  policy-create request gained only optional bilingual descriptions
  (contract-mandated, additive — flagged in the report), no other
  policy-admin behavior touched. Report also flags: a pre-existing
  date-dependent `PlatformChargeRuleTests` time-bomb was repaired
  (test-only, hardcoded 2026-10-10 schedule date became the past).

- [x] Done — **Phase 7. Docs + Postman sync and gates.**
  Owns: doc/collection files only. Full solution build (0 warnings) +
  full unit + architecture suites once, `git diff --check`, Postman JSON
  validation. No provider calls, no migration applies, no seed data.
  Gates: build 0W/0E; unit 2691 passed / 0 failed / 3 pre-existing static
  skips; architecture 1/1; all 20 Postman JSONs valid. Doc sync: stale
  payout-policy create body in `finance-platform-charges.md` + two
  document-map rows; one-line xUnit2013 analyzer fix in the phase-6 test
  file (see report).

Done criteria per phase: zero-warning build, focused tests green, docs +
Postman synced, branch + SHA reported. State-changing checks only on
authorized disposable targets with fixture-first preparation.

## Ready for integration (Integrator-owned discovery)

A phase is mergeable only if ALL hold: its box below is checked, its report
file `docs/opus/reports/<slug>.md` exists with branch + base/final SHAs +
gate evidence, its dependencies are already in `main`, and the branch tip
equals the reported final SHA. Otherwise it is skipped as blocked (never
force-merged). On owner trigger "integrate now", the Integrator scans these
boxes, merges qualifying phases in numeric order, marks each merged box with
the `main` SHA, and pushes `main` once. Workers never merge or push; workers
write the report file as part of Done.

- [x] phase-1-rating — report: `docs/opus/reports/phase-1-rating.md` (merged into `main` via `2db70cc`)
- [x] phase-2-categories — report: `docs/opus/reports/phase-2-categories.md` (merged via `2db70cc`)
- [x] phase-3-search — report: `docs/opus/reports/phase-3-search.md` (merged via `2db70cc`)
- [x] phase-4-chat-deltas — report: `docs/opus/reports/phase-4-chat-deltas.md` (merged via `2db70cc`)
- [x] phase-5-library — report: `docs/opus/reports/phase-5-library.md` (merged via `2db70cc`)
- [x] phase-6-payout-reads — report: `docs/opus/reports/phase-6-payout-reads.md` (merged via `2db70cc`)
