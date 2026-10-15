# PORTAL-07 report — final integrated gates and frontend handoff (no deployment)

Status: local worker delivery complete, pending mastermind acceptance.
No publish, deployment, VPS access, provider send, Bruno, CORS, migration,
reset/seed on any persistent target, or new package occurred in the valid
gate run. PORTAL-06 stays conditional; external items stay explicitly open.

## Intake

- Worker base: pushed `main` `5dff36ccd40016ffb82eebe5a6078795941c7d68`
  (PORTAL-05 acceptance + PORTAL-07 brief). At intake `main == origin/main`
  with a clean tree; no reset performed.
- Owned manifest (exact files created/modified, nothing else):
  - new `docs/internal/work/portal-api-reconciliation/reports/phase-07-integration.md`
    (this file)
  - new `docs/internal/work/portal-api-reconciliation/frontend-handoff.md`
  - `docs/internal/work/portal-api-reconciliation/plan.md` (PORTAL-07 status
    lines only)
- Production/test code is read-only in this task and was not touched. No
  documentation/Postman correction beyond the owned manifest was needed: no
  new concrete mismatch was found during reconciliation.
- Central `Mastermind_Handoff.md` / `Project_Main_Goal.md` belong to the
  mastermind and were not edited.

## Safe readiness (before any gate host/scenario)

1. Inspected existing tests, configuration, and prior readiness evidence.
   No external database is needed for these gates; no fixture rows are
   needed (metadata checks are row-free; unit tests carry per-test
   InMemory/disposable fixtures).
2. Integration keys in current use: exactly
   `ConnectionStrings__IdentityIntegrationDatabase` and
   `ConnectionStrings__FamiliesIntegrationDatabase` (guards:
   `LocalPostgresFactAttribute`, `ChatPostgresFactAttribute`,
   `ChatPostgresCleanupFixture`, `LocalPostgresIdentityFixture`,
   `SubscriptionTaxPostgresConcurrencyTests`; all skip on blank via
   `IsNullOrWhiteSpace`, and all `EnsureDeleted` calls sit behind those
   guards). Both keys were kept absent/blank in the valid gate child
   process via process-only overrides in the same invocation (machine/user
   environment untouched; no secret read or printed).
3. API test factories inspected: `PortalApiMetadataHost` (dummy unreachable
   targets for all nine module keys, InMemory re-registration, no jobs,
   ephemeral keys, temp storage), `AuthApiHostTests.SanadApiFactory`
   (non-serving loopback `Host=localhost;Port=1` override), owner-portal
   fixture (fresh InMemory store per test), others use capturing senders.
   No factory can reach an inherited real database; no test opens the
   inherited `IdentityDatabase` connection (all references use dummy
   values). Migrations/seeders stay off in every host; no startup
   migration/seeding is used as setup.
4. Expected skips recorded in advance: 5 PostgreSQL integration cases via
   the documented guards + 3 pre-declared Families legacy skips.

## Integrated gates (exact commands, valid run)

- Strict build `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true`: exit 0, **0 warnings, 0 errors**.
- Full unit `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj
  --no-build --no-restore --nologo -v minimal` (safe child setup above):
  **3308 passed, 0 failed, 8 skipped, total 3316**. Skip groups: 3
  pre-declared Families legacy skips (`SubscriptionTaxRuleTests` ×2
  conflict-mapping ownership, `SubscriptionTaxPostgresConcurrencyTests` ×1
  legacy-table fixture; all `Fact(Skip=...)`) + 5 PostgreSQL integration
  skips via documented env guards (2 `IdentityPostgresIntegrationTests`
  via `LocalPostgresFact`, 3 `AttachmentCleanupPostgresTests` via
  `ChatPostgresFact`). No invented total, no suppression, no exclusions.
- Architecture `dotnet test tests/Sanad.ArchitectureTests/
  Sanad.ArchitectureTests.csproj --no-build --no-restore --nologo
  -v minimal`: **1/1**, zero failures.
- Postman: all 22 `docs/postman/**/*.json` files parse (no send);
  `docs/internal/tools/Verify-ApiContractMapping.ps1`: **556 controller
  signatures / 620 Postman requests / 0 missing / 0 orphan**, exit 0.
- `git diff --check`: clean.
- Diff since audit baseline `8cbeedd`: no `*Migrations*`, no
  `*appsettings*` change — no new portal migration and no new mandatory
  runtime configuration from this slice (persistent DataProtection/storage
  and other existing deployment requirements still apply unchanged).

## First-run deviation (disclosed, not used as evidence)

The first full-suite invocation ran with the inherited
`IdentityIntegrationDatabase` still set (the blanking did not propagate
across separate shell invocations): 3313 passed / 0 failed / 3 skipped,
with the 5 PostgreSQL-guarded tests executing their fixture
`EnsureDeleted`/`EnsureCreated` cycles on the local `SanadIntegrationDb`
and the separate local `SanadChatCleanupTestDb` only. No production, VPS,
or development database (`SanadDb`) was contacted; no other target was
written. That result is discarded as gate evidence. The valid rerun above
uses single-invocation process-only blanks (verified blank in the same
call before `dotnet test`) and performs zero resets and zero external
database contact.

## Finding disposition (final)

- PORTAL-01 reactivation 409 `Identity.Admin.ActivationNotReady`: accepted,
  covered (phase-01 report). No forced activation; verification preserved.
- PORTAL-02 library casing/validation: accepted with mastermind correction
  (strict numbers preserved); covered (phase-02 report).
- PORTAL-03 metadata (28 schemas, required queries, enums, wrapper IDs,
  community-update DTO, finance 204/200): accepted with corrections;
  covered (phase-03 report).
- PORTAL-04 caregiver diagnostic: accepted; deployed six-row cause/count
  explicitly unverified, operator runbook delivered, no repair (phase-04
  report). Still open pending operator evidence.
- PORTAL-05 owner routes (seven GETs + settings PUT, isolation, versions,
  Draft/Approved, 401/403): accepted after strengthening (19 owner tests);
  covered (phase-05 report). One authored-example defect corrected
  (numeric `day`); no production-code defect found.
- PORTAL-06 geography: conditional, not started, not included here.
- No new mismatch requiring a bounded correction handoff was found during
  this reconciliation; production/test code needed no change.

## Open external items (explicitly not complete, not waived)

- Deployed caregiver six-row integrity cause/count unverified (diagnostic +
  operator runbook delivered; repair is its own reviewed change).
- PORTAL-06 dataset approval/import and any target data change.
- Untested nested/detail/mobile endpoints and deferred landing-media
  cleanup (separate low-priority design).
- Separate Chat Plus runtime/device/soak gates (Prompt 7).
- Production revision remains last verified `8cbeedd` until the owner
  reports a new deployment; nothing here proves deployed behavior.

## Publish/deploy handoff (orders only, nothing run)

Package a later source revision only on owner order; after owner
deployment, check revision/migrations/smoke, then use HTTPS or an approved
SSH tunnel with designated test actors for authenticated verification
(no sensitive token over public HTTP). No deploy commands run now.
