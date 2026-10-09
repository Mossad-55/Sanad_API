# Opus work package — task checklist

Single implementation checklist for the Opus scope (library, payout reads,
community additions, rating unification). Chat is excluded. Statuses: Not
started, In progress, Blocked, Done. The contract in `plan.md` is frozen;
changing it needs a new explicit owner decision. File manifests are
exclusive: Opus edits only the files below plus the listed docs/collections.

## Order (dependency-ordered, commit per phase locally, no push/merge/deploy)

- [ ] Not started — **Phase 0.** Branch `opencode/opus-work` from latest `main`; verify clean base.
  Owns: new branch only.

- [ ] Not started — **Phase 1. Rating unification.**
  Owns: `Modules/Community/Application/Ratings/AddRatingCommand.cs` (+handler
  range validation), `Sanad.API/Controllers/CommunityPostsController.cs`
  (binding only), `tests/.../Community/*Rating*`, Postman community requests,
  `docs/app/community/writes.md` touch-up.

- [ ] Not started — **Phase 2. Community categories (+migration).**
  Owns: `Modules/Community/Domain` (category entity),
  `Modules/Community/Application` (category lookups + admin CRUD),
  `Modules/Community/Infrastructure` (config + additive migration authored
  only, DI untouched), `Sanad.API/Controllers` (admin category endpoints +
  public list), tests, docs, Postman.

- [ ] Not started — **Phase 3. Community search/filter/sort + favorites + per-user state.**
  Owns: `GetPostsQuery` extension, new favorites-list query,
  `CommunityPostResponse` additive fields, `CreatePostCommand` (+`categoryId`),
  controller query params, tests, docs, Postman. Moderation untouched.

- [ ] Not started — **Phase 4. Chat deltas.**
  Owns: `Modules/Chat/*` deltas (booking-open resolution, Location type +
  validation, POST read, attachment route + `contactUserId` on facility
  detail, accountType/care-home resolution), `Sanad.API/Controllers/Chat*`,
  tests, `docs/chat/*` deltas, Postman chat deltas. Frozen chat rules
  (1:1, send-only, polling, no groups) are not renegotiated here.

- [ ] Not started — **Phase 5. Library in CMS (+migration).**
  Owns: `Modules/Cms/{Domain,Application,Infrastructure}` (categories,
  content, sections, saved/liked/views), `Sanad.API/Controllers`
  (public + `CmsContent` admin endpoints), cover upload handling, tests,
  new `docs/app/library/` guide, Postman App library folder.

- [ ] Not started — **Phase 6. Caregiver payout reads.**
  Owns: `Modules/Caregivers/Application` (summary/list/detail/policy-read
  queries), `Modules/Finance` (policy description fields + migration),
  `Sanad.API/Controllers` (caregiver payout reads), tests, `payouts.md`
  deltas, Postman deltas. No ledger/transfer/policy-admin changes.

- [ ] Not started — **Phase 7. Docs + Postman sync and gates.**
  Owns: doc/collection files only. Full solution build (0 warnings) +
  full unit + architecture suites once, `git diff --check`, Postman JSON
  validation. No provider calls, no migration applies, no seed data.

Done criteria per phase: zero-warning build, focused tests green, docs +
Postman synced, branch + SHA reported. State-changing checks only on
authorized disposable targets with fixture-first preparation.
