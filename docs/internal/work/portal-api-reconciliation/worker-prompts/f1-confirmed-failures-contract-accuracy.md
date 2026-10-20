# F1 — Confirmed failures and consumer contract accuracy (ACTIVE WORKER BRIEF)

## Read first

1. `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md`.
3. Approved scope: `docs/internal/work/portal-api-reconciliation/contracts/frontend-follow-up-2026-10-20.md`, F1 section.
4. Evidence map: `docs/internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md`, A4 items #34 and related check-in/SOS/help-request notes. The old frontend request itself is not in this checkout; do not invent additional reported gaps.
5. Inspect the pinned base and live worktree before editing; preserve unrelated work.

## Current checkpoint

- Phase state: `completed`; Mastermind accepted F1 and the Owner-authorized
  commit/push/deployment workflow is verified. Source commit
  `0dbb74bc9f43e4d9ed586bd5a584b6efaf826f2c` is pushed to `origin/main`; the VPS
  reports that exact revision. Release is verified. No F1 migration was added or
  applied; no provider call or production-data mutation was performed.
- Base revision: `d81afb122ed368e630f0b7d5e5da4ee47fbf5325` (`main`, pushed).
- Pre-existing modified/untracked work remains in `.codex/`, `README.md`, the
  handoff/governance, E05/E06 proposals, bridge tooling, audit files, follow-up
  contract/clarifications and E03 prompts. None is F1-owned; preserve it and
  exclude it from F1 changes.
- Worker: Owner-selected `river/deepseek-ai/DeepSeek-V4.1-Flash#high`, reused from
  L1. Session `ses_ea6ceaf39ffeReOQqIAYTGkZ1G` began the phase; its runtime later
  returned `No ready deployment`. Mastermind continued the same approved phase;
  no provider/model switch was made.
- L1 is completed and pushed. Reuse its local persistent-fixture approach only
  within its guardrails; do not change L1 files or rerun unrelated checks.

## Objective and boundaries

Implement only the approved F1 package. Reproduce the elderly-ID check-in list/
aggregate, Admin SOS, and Admin help-request filters against PostgreSQL before
claiming a query-translation defect. Fix only confirmed affected query paths;
preserve role policy, visibility, payloads, enum/ID wire conventions and audit
behavior. Do not claim `.Value` as the cause without reproduction.

Make unsupported FAQ audience input fail with a stable coded validation response
until the separately scoped F5 support work exists. Supported audience values
remain Family=1, MedicalCaregiver=2, CompanionCaregiver=3, Elderly=4. Do not add
CareHomeOwner FAQ support, audience switching, or any unanswered frontend
addition.

Publish actual success DTOs/statuses for the finite remaining A4 #34 groups
listed below. Compare source, generated OpenAPI, current docs and Postman before
editing. Do not assume missing metadata from the old report remains missing;
reuse correct metadata and touch only confirmed gaps. Document current bounds
and exact inventory conflict semantics without changing inventory policy unless
a source-confirmed defect is reproduced. No global serialization/error rewrite,
new endpoint, migration, seed framework, broad backend cleanup, or unrelated
behavior changes.

## Exact owned-file boundary

Only edit these files if the inspected current source confirms the change is
needed; add a focused test under the listed test directories only when required.

### Query, validation and HTTP metadata

- `src/Modules/Families/Application/Sanad.Modules.Families.Application/CheckIns/AdminElderlyCheckInQueries.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Sos/SosApplication.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/HelpRequests/ElderlyHelpRequestApplication.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/CreateHelpFaqCommandValidator.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/UpdateHelpFaqCommandValidator.cs`
- `src/API/Sanad.API/Controllers/AdminElderlyHelpRequestsController.cs`
- `src/API/Sanad.API/Controllers/CareHomeInventoryController.cs`
- `src/API/Sanad.API/Controllers/CareHomeVisitsController.cs`
- `src/API/Sanad.API/Controllers/CareHomeFinanceController.cs`
- `src/API/Sanad.API/Controllers/NotificationsController.cs`
- `src/API/Sanad.API/Controllers/AdminFinanceController.cs`

### Focused tests

- `tests/Sanad.UnitTests/Families/CheckIns/AdminElderlyCheckInQueryTests.cs`
- `tests/Sanad.UnitTests/Families/Sos/ElderlySosTests.cs`
- `tests/Sanad.UnitTests/Families/HelpRequests/ElderlyHelpRequestTests.cs`
- `tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`
- `tests/Sanad.UnitTests/API/AdminElderlyCheckInsControllerTests.cs`
- `tests/Sanad.UnitTests/API/AdminElderlyHelpRequestControllerTests.cs`
- `tests/Sanad.UnitTests/API/AdminFinanceControllerTests.cs`
- `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`
- new `tests/Sanad.UnitTests/Families/AdminElderlyFilterPostgresTests.cs` for
  real-provider reproduction; it must use a verified unique disposable DB and
  never reset persistent `SanadDb`.

### Consumer documentation and Postman, only for confirmed affected contracts

- `docs/admin/elderly-check-ins.md`
- `docs/admin/elderly-sos.md`
- `docs/admin/elderly-help-requests.md`
- `docs/admin/help-center.md`
- `docs/care-homes/inventory-and-availability.md`
- `docs/care-homes/booking-lifecycle.md`
- `docs/care-homes/finance-reporting.md`
- `docs/app/notifications.md`
- `docs/admin/finance-platform-charges.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`
- `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- `docs/postman/app/Sanad.App.Elderly.postman_collection.json`

No other files without Mastermind approval. Do not edit frontend clarifications,
the approved contract, L1 brief, README, unrelated docs/configs, migrations,
environment files, or unrelated Postman collections.

## Acceptance matrix

### A. PostgreSQL query reproduction and regressions

1. For Admin check-in list and aggregate, exercise a valid existing Elderly GUID,
   a valid unknown GUID, an empty-result case, and unfiltered control. Verify
   filters and counts are correct and each authorized access still writes its
   expected payload-free audit record. Cover date bounds as relevant.
2. For Admin SOS and help-request lists, exercise real and unknown Elderly GUID
   filters and empty-result cases; verify no cross-Family/deleted-Family records
   leak and the expected audit rows remain.
3. Run on PostgreSQL for the translation/persistence claim. Record exact query,
   target classification, safe fixture identity labels (never credentials),
   result and access-audit readback. Do not use production/VPS. Do not reset or
   bulk-seed persistent `SanadDb`; reuse suitable synthetic rows or stop if the
   required data is unavailable. Existing `SanadBrunoTestDb` test guard remains
   intact.
4. If PostgreSQL does not reproduce a suspected defect, leave query behavior
   unchanged and report the evidence; do not rewrite expressions speculatively.

### B. FAQ unsupported audience

5. Create and update requests for each supported value 1–4 retain existing
   behavior. Undefined and defined-but-unsupported audience 5 return a coded
   validation response (expected request field `audience`), not an unexpected
   500. Verify no FAQ row is created/modified on rejection. Do not implement F5.

### C. Confirmed OpenAPI/document/Postman gaps (A4 #34 finite list)

6. Admin elderly help-request detail/history publish actual success DTOs/statuses.
7. Owner inventory operations publish their real results: inventory reads and
   business mutations return `CareHomeInventoryDto`/200; media uploads return
   their media DTO/201; media removal remains empty/204; archive and maintenance
   operations retain their actual response semantics.
8. Owner and Admin inventory availability are POST operations returning the
   actual availability list/200, not GET.
9. Owner booking-request list publishes the actual list item response.
10. Owner visits list and visit-settings GET/PUT publish actual DTOs and referenced
    enums; do not treat booking visits, receipts, internal notes, or operational
    views as one booking DTO.
11. Owner dashboard/revenue publish their actual DTOs; CSV export retains file
    content/download semantics.
12. `GET /api/v1/notifications/unread-count` publishes the actual bare integer,
    not a `{count: ...}` wrapper.
13. `POST /api/v1/admin/finance/platform-charge-rules` publishes actual 201 with
    a GUID response, not a default 200/object.
14. Verify generated OpenAPI and changed docs/Postman agree with controller
    source/serialization. Preserve numeric enum JSON and current wrapper/plain
    GUID conventions.

### D. Inventory bounds and conflict behavior

15. Compare current inventory source, OpenAPI, guide and Owner/Admin Postman.
    Document only verified limits and existing missing/duplicate/occupied,
    maintenance-overlap, stale-version/concurrency conflict codes. Do not claim
    all old frontend gap counts exist. Change runtime behavior only for a
    reproduced, contract-confirmed defect and add focused regression coverage.

### Gates and handback

- Focused relational regressions, controller/OpenAPI tests, affected script/docs
  validation and Postman JSON validation must pass. Build/test gates exit 0 with
  zero warnings. No automatic full suite; Mastermind chooses any additional
  bounded gate after reviewing risk.
- Distinguish authored versus executed checks and real PostgreSQL evidence versus
  InMemory/unit behavior. Never invent live results.
- Update only this active brief's current result/task state. Worker may mark
  `ready_for_review`, never `completed`. After Mastermind acceptance, follow
  Owner's standing release direction: commit/push exact F1 files only and deploy
  if accepted changes alter Production runtime behavior. Mastermind verifies the
  exact package/revision and target; no migration or production-data operation
  without explicit scope-specific authorization.
- No unresolved frontend additions, OTP, production writes, provider calls,
  migration/reset, credentials in logs/reports, or unrelated work.

## Worker and current result

- The Worker initially ran PostgreSQL tests using fixed `SanadF1VerifyDb` with
  `EnsureDeletedAsync()` before migrations. Whether that fixed database held
  pre-existing data is **unknown**; the Worker reported no `SanadDb` touch, but
  that report is not independently verifiable. Two read-only attempts to inspect
  the old fixed target failed to establish its current existence/contents (the
  first could not load Npgsql; the second connection/query failed with details
  suppressed). No write or migration was performed by those inspections; no
  further operation targeted that fixed database. Owner explicitly authorized
  treating this specific fixed test target as disposable; its former contents
  remain unknown and no claim is made about them. The corrected tests use a fresh
  unique target and do not touch persistent `SanadDb` or the fixed-name target.
- Mastermind replaced the unsafe test target: require loopback PostgreSQL port
  5432, generate a unique nonce-bearing database name, prove that exact target
  absent before creating it, apply Families migrations only there, and delete
  only that test-run-owned target at teardown. No `SanadDb` or fixed-name target
  is used by the corrected tests.
- PostgreSQL reproduction: the exact old `.Value` ID predicates for Check-in,
  SOS and help-request query forms each fail translation with PostgreSQL; the
  corrected handlers pass real/unknown/empty-filter, deleted-Family visibility
  and payload-free audit assertions. The test uses its own unique disposable DB.
- FAQ create/update now validate `IsHelpAudience`; supported audience values
  remain unchanged, while `CareHomeOwner` and undefined numeric values produce
  `400 Api.Validation.Failed` with an `Audience` field error before persistence.
- Source review found the finite A4 #34 success metadata was already present for
  help-request detail/history, owner inventory mutations/media/availability,
  booking-list, visits/settings, dashboard/revenue, unread count and charge-rule
  creation. Added generated-OpenAPI assertions for those DTO/statuses and the
  bare integer/201 GUID/204-no-body shapes. Corrected the platform-charge current
  endpoint to return 204 when no effective rule exists (it previously returned
  `Ok(null)` despite the documented 204 contract); retained populated 200.
- Updated `docs/admin/help-center.md` with unsupported-audience validation,
  `docs/care-homes/inventory-and-availability.md` with source-verified conflict
  codes/statuses, and the Finance Postman description to remove its contradictory
  current-rule response claim. Other listed consumer docs and Postman requests
  were inspected and already match current behavior.
- Final focused Release gate with `TreatWarningsAsErrors=true`:
  157 passed / 0 failed / 0 skipped, including 8/8 real PostgreSQL tests against
  the unique disposable target. Build succeeded with zero warnings. Postman JSON
  parsed for six affected collections; modified Markdown links validated;
  `git diff --check` passed.
- The Worker-reported `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`
  failure was excluded from the final focused gate; its baseline status remains
  Worker-reported and was not independently rerun by Mastermind.
- F1 changed files: three Families query files, two FAQ validators,
  `AdminFinanceController.cs`, `AdminFinanceControllerTests.cs`,
  `HelpCenterTests.cs`, `PortalOpenApiContractTests.cs`, new
  `AdminElderlyFilterPostgresTests.cs`, `docs/admin/help-center.md`,
  `docs/care-homes/inventory-and-availability.md`, this brief, and
  `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`. Unrelated
  work remains untouched.
- Release receipt: implementation commit/manifest revision is
  `0dbb74bc9f43e4d9ed586bd5a584b6efaf826f2c`, pushed to `origin/main` and
  confirmed by `git ls-remote`. Archive `deploy/f1-0dbb74b.tar.gz`: 11,885,926
  bytes, SHA-256 `86B11A2F81ADF9571B041BD47DF885CB260EF14FB4BAF6AEC65CCE472D259F18`;
  excluded appsettings, `.env`, local settings, keys and certificates. Deployed
  only to the previously authorized Production target `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`.
- Preflight was active at E04 revision `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9`,
  Production, DB reachable; all nine applied migration heads already matched the
  F1 manifest. A private binary/config backup was made. Startup applied zero
  migrations across nine contexts. Post-deploy service is active, `NRestarts=0`,
  revision matches F1, DB is reachable, host service/environment/appsettings
  fingerprints are unchanged, and `Verify-SanadDeployment.ps1` passed 9/9 heads
  and anonymous lookup smoke HTTP 200. Temporary SSH tunnel was closed. No
  authenticated Production feature request or provider call was made.
- Next action: report and stop. F1 is deployed and verified; do not start F2 or
  another phase without new Owner direction.
