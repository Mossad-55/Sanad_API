# PORTAL-07 — final integrated gates and frontend handoff (no deployment)

Role: bounded integration-verification/documentation worker. PORTAL-01 through
05 are reviewed/accepted. PORTAL-06 is conditional on owner-approved geography
data and is **not** silently included here. Do not implement new product work.

## Intake / baseline / ownership

Read AGENTS.md, governance, Mastermind_Handoff and the active portal plan.
Read only this slice's final report appendices/known limitations and current
diffs needed to reconcile evidence. Previous bounded mastermind reviews are
complete; do not restart a repository-wide audit or duplicate the worker chain.

Phase 05 worker delivery was `6b3290646e6c79ad3928f9e199dfa570b7039031`.
The following mastermind commit adds approved-facility/no-facility coverage,
replaces a flaky substring assertion, fixes the test's Cairo payment-day choice,
records acceptance and adds this brief. Use the launch message's exact final
pushed SHA, verify clean main equals origin/main and record it; do not reset.

Work directly on main, one worker at a time. You are not alone: preserve others'
edits and stage explicit owned paths. No branches/worktrees, hook bypass, merge,
publish/package generation, deployment, live VPS calls, provider sends, Bruno,
CORS change, migration/reset/seed on any persistent target or new packages.

Owned files: new `../reports/phase-07-integration.md`, new
`../frontend-handoff.md`, and only PORTAL-07 status/evidence plus final finding
dispositions in `../plan.md`. Correct affected documentation/Postman examples
only for a concrete evidenced mismatch; identify exact extra paths before edit.
Central Mastermind_Handoff/Project_Main_Goal belong to the mastermind.
Production/test code is read-only unless a concrete failure requires a separately
bounded correction handoff. Do not suppress, skip or weaken a failing check.

## Mandatory safe readiness before any gate host/scenario

1. Inspect the existing tests/configuration/readiness evidence first. Prepare
   process-local safe overrides before running the unit executable. Do not
   read or print secrets or inherit an unchecked database/provider/storage
   target. No external database is needed for this gate.
2. The inherited PostgreSQL integration fixtures contain EnsureDeleted calls.
   Keep **all integration connection keys absent/blank in the child test
   process**, including IdentityIntegrationDatabase and
   FamiliesIntegrationDatabase; inspect for any other integration key used by
   current tests. Let their documented guards skip those tests. Do not select
   a real target merely to make the skip count zero. Do not alter machine/user
   environment settings; use process-only overrides and restore safely.
3. Inspect existing API test factories beyond the new portal host as well.
   Main module keys (Identity/Cms/Caregivers/Families/CareHomes/Notifications/
   Community/Chat/Finance) must not resolve to a real inherited database in
   any gate process. Use deterministic non-serving loopback test settings for
   legacy registration if needed, with real background/provider work suppressed
   by the selected host. Configure migrations/seeders off, ephemeral or
   test-only keys, safe temporary roots, fake providers, synthetic JWT settings
   before startup. If a factory cannot guarantee isolation, report its exact
   dependency and request a bounded test-host correction; do not run it blindly
   or silently drop that class from the full-suite claim.
4. Reuse existing per-test fixtures; confirm that actual actor/state/relations
   are prepared/read back before requests. Do not add unrelated global seed data
   or use startup migration/seeding as setup. Metadata checks need no DB rows.
   Record safe key names/provider choices and expected skips, not secret values.

## Integrated verification

- One strict final `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true`: successful exit, **zero warnings/errors**.
- One full unit run `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj
  --no-build --no-restore --nologo -v minimal` under the verified safe process
  setup. Record actual passed/failed/skipped counts and explain each skip
  group. No invented total, blanket warning suppression or arbitrary filter
  exclusions. At the first concrete failure, capture exact evidence and scope;
  do not rerun unchanged failures or start a broad refactor. Final code fixes,
  if separately approved, invalidate the relevant build/test inputs.
- Architecture suite `--no-build --no-restore`: 1/1, zero warnings/failures.
- Parse repository Postman collection JSONs without sending them. Run the
  Postman-only `docs/internal/tools/Verify-ApiContractMapping.ps1`: zero
  missing/orphan routes. Check documentation links changed in this slice and
  `git diff --check`.
- Reconcile the original report against accepted source changes and final test
  evidence. Do not infer deployed verification from local tests. The current
  Production revision remains last verified `8cbeedd` unless the owner supplies
  a new deployment report; do not probe/update it as part of this task.

## Frontend handoff and final disposition

Create a concise actionable `frontend-handoff.md` containing:

- Reactivation: readiness failure 409 `Identity.Admin.ActivationNotReady`, safe
  detail/trace; verification requirements preserved, no forced activation.
- Library: camelCase and legacy PascalCase accepted, strict numeric sections,
  Article/Video rules, field/indexed errors, omission versus empty sections,
  preserved type/cover/version/lifecycle behavior; point to canonical examples.
- OpenAPI: all 28 actual success schemas; five required query flags; numeric
  enum values plus names; existing wrapped/plain IDs preserved; route-owned
  community update Id; current charge-rule empty read **204**, populated 200.
- Care Homes owner: seven GETs/settings PUT verified locally with real handlers,
  Draft and approved states, actual ownership/version/date/empty outcomes;
  numeric `day` (1 Monday), not string names. Provider/concurrency limits explicit.
- Intentional unchanged behaviors: unset support contact 404, app-only help/legal
  admin 403, existing wire enum/ID conventions, owner-confirmed CORS.
- Open external items: deployed caregiver integrity cause/count unverified
  (link diagnostic/operator runbook); PORTAL-06 dataset approval/import separate;
  untested nested/mobile endpoints and deferred media cleanup; separate Chat
  runtime/device/soak gates. Do not call these complete or silently waive them.
- No new portal migration or new mandatory runtime configuration from these
  changes. Verify this against the actual diff; do not contradict existing
  deployment requirements (persistent DataProtection/storage etc. still apply).
- Exact publish/deploy handoff **only**: source revision to package on a later
  owner order; after owner deployment check revision/migrations/smoke, then use
  HTTPS or approved SSH tunnel with designated test actors for authenticated
  verification. No sensitive token over public HTTP. No deploy commands run now.

Report changed docs, base/final SHA, exact gate commands/results/warnings/skips,
fixtures/configuration safety and the frontend finding-disposition matrix.
If required gates pass, mark local PORTAL-07 work delivered pending mastermind
acceptance, with external items explicitly open; do not mark global production
readiness as proven. If a gate cannot safely run, state the precise limitation
instead of passing it. Commit/push normally and stop for mastermind review.
