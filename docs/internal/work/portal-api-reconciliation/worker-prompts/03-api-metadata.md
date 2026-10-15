# PORTAL-03 — accurate OpenAPI contracts without a wire-format migration

Role: bounded implementation/test/documentation worker. Execute only the
approved PORTAL-03 scope, then return for mastermind review. PORTAL-01/02 are
accepted; do not start caregiver diagnostics, owner-portal verification or
geography provisioning.

## Intake and pinned baseline

Read root AGENTS.md, governance, Mastermind_Handoff, PORTAL-03 and the success
inventory/readiness section of `../plan.md`. Inspect the affected controllers,
actual returned DTOs, serialization setup and API test hosts before editing.

Phase 02 worker delivery was `496205363a6f7060d26c5e240b14b22747379de6`.
The following mastermind commit corrects unintended quoted-number acceptance,
adds regressions/literal examples, records acceptance and adds this brief.
The launch message pins that final pushed SHA: verify clean main equals
origin/main at that revision and record it. Do not reset to the older delivery.

Work directly on main per current owner policy; one worker at a time. You are
not alone in the codebase: preserve others' edits, stage explicit owned paths,
and stop on overlapping unowned changes. Do not create branches/worktrees,
merge, publish, deploy or bypass hooks (`--no-verify`). Commit and push normally
only after clean required checks. Return the exact final SHA.

## Approved outcomes

1. Publish real success response schemas for all **28 operations** in the
   plan's required inventory (22 GETs and six admin-user actions). Determine
   the actual DTO/envelope, nested records, collection element types, nullable
   properties and status from controller + handler + mapper. The controller
   returns `result.Value`, not a `Result<T>` envelope. Create-admin is **201**,
   not an invented 200. Preserve and document any actual 204/no-body first-run
   state and existing 404s; do not force empty states into invented DTOs.
2. Mark the five existing required query parameters as required in generated
   OpenAPI: `through` on admin care-home license-expirations; `governorateId`
   on public/admin cities; `cityId` on public/admin areas. Preserve business
   requirements and existing validation codes/outcomes. Prefer metadata-only
   corrections where possible; do not change omission behavior just to fix
   the specification. Verify supplied unknown parents separately from missing,
   empty, malformed and Guid.Empty values. Use source-supported expectations,
   not an assumption that every invalid/unknown case has the same outcome.
3. Numeric enum schemas expose their **actual CLR numeric values and names**,
   including UserStatus, AccountType, DevicePlatform, CaregiverType and
   AdminUserActionType. Derive the mapping from enum definitions, not the
   frontend's guessed list. Use descriptions plus a documented extension such
   as `x-enumNames` aligned with `enum` values (and aliases handled explicitly).
   Nullable enums retain nullability; flags, if encountered, must not acquire
   a constraint that incorrectly rejects valid combinations. Existing string
   enums stay strings. String-valued filters document their actual accepted
   names and defined numeric-string compatibility without changing binding.
4. Strongly typed GUID IDs that currently serialize as `{ "value": "..." }`
   keep that wire shape, with their actual non-null `value` required in the
   schema. Plain Guid DTO properties remain strings. Limit the correction to
   verified ID wrapper types, not every arbitrary DTO with a `value` property.
   Do not introduce a global converter or flatten existing response IDs.
5. Community category update uses a dedicated body DTO without the redundant
   command Id. Construct UpdateCategoryCommand with the **route** categoryId.
   Omitting body Id works, and a legacy conflicting body Id remains ignored,
   as it is today. Preserve all other fields/defaults, auth and outcomes.

Keep error handling, authorization, lifecycle, runtime JSON options, global
enum/ID wire formats, queries/handlers/domain and database mappings unchanged
except the narrowly approved community body-to-command mapping. Existing
SupportContact-unset 404 and app-only help/legal admin 403 remain intentional.
CORS remains owner-confirmed unchanged. Bruno is excluded.

## Owned manifest

Production controller files under `src/API/Sanad.API/Controllers/`:

- `AdminUsersController.cs` — the ten listed reads/actions only.
- `AdminElderlyCheckInsController.cs`, `AdminElderlyHelpRequestsController.cs`,
  `AdminElderlyMedicationsController.cs`, `AdminElderlySosController.cs`.
- `AdminCareHomeCheckInDisputesController.cs`, `AdminElderlyWelcomeController.cs`,
  `AdminFinanceController.cs`, `AdminNotificationsController.cs`,
  `AdminSentenceBuilderCatalogController.cs`, `AdminWellnessTipsController.cs`,
  `CareHomeInventoryController.cs` — only the inventoried reads.
- `AdminCareHomesController.cs`, `AdminLookupsController.cs`,
  `LookupsController.cs` — only the five required-query declarations/metadata.
- `AdminCommunityCategoriesController.cs` and new
  `Controllers/Requests/UpdateCommunityCategoryRequest.cs` (or the equivalent
  single request record in that controller).

Other owned paths:

- `src/API/Sanad.API/DependencyInjection.cs`: only OpenAPI registration; do not
  edit CORS or unrelated configuration.
- New narrowly scoped OpenAPI transformer/configuration files under
  `src/API/Sanad.API/OpenApi/`. Use the installed OpenAPI packages; no package
  upgrade or new dependency is authorized.
- `tests/Sanad.UnitTests/API/AdminResponseSchemaContractTests.cs` and new
  `PortalOpenApiContractTests.cs`, `PortalApiMetadataHost.cs`,
  `AdminCommunityCategoryRequestTests.cs` under that API test directory as
  needed. Existing AuthApiHostTests is a read-only pattern, not permission to
  alter unrelated auth tests. Keep test scaffolding bounded and private.
- Corresponding affected guides only: `docs/admin/user-management.md`,
  `elderly-check-ins.md`, `elderly-help-requests.md`, `elderly-medications.md`,
  `elderly-sos.md`, `elderly-welcome.md`, `finance-platform-charges.md`,
  `notifications.md`, `wellness-tips.md`, `lookups-cities-areas.md`;
  `docs/app/public/lookups.md`, `docs/app/community/writes.md`,
  `docs/care-homes/inventory-and-availability.md`, and new
  `docs/admin/api-contract-conventions.md` for shared
  enum/ID/filter metadata conventions. Do not rewrite every guide.
- Only affected requests/descriptions/examples within existing
  `docs/postman/admins/Sanad.Admin.postman_collection.json`,
  `docs/postman/app/Sanad.App.Public.postman_collection.json` and
  `docs/postman/care-homes/Sanad.CareHomes.postman_collection.json`. No full
  collection reformat or unrelated request additions.
- `../reports/phase-03-api-metadata.md` and only PORTAL-03 status/evidence in
  `../plan.md`. Mastermind owns central handoff/index.

Before edits, report the exact selected manifest, including real guide and
Postman paths. Ask for a bounded extension if an essential unlisted production
file is needed. No Program/startup redesign, migration, seed/import job,
caregiver filtering/repair or other behavior fix belongs here.

## Mandatory readiness BEFORE API startup/scenarios

1. Inspect existing configuration/test hosts and fixture helpers read-only.
   Use an isolated in-process fake-handler/test-auth host. Explicitly replace
   inherited connection/provider/storage settings, disable startup migrations,
   test-user seeders and all real background/provider work before host startup.
   Do not connect even to a dummy PostgreSQL target or inherit secrets.
2. Metadata GETs need no data fixtures: state that explicitly, prepare the
   fake/configuration setup first, then generate the real document through
   `/openapi/v1.json`. Reuse the production OpenAPI registration/controllers;
   do not substitute a handwritten schema or a test-only corrected document.
3. For sample response/route tests, prepare deterministic fake DTOs or minimal
   isolated InMemory fixtures first, inspect/read back their relationships and
   states, and record the readiness manifest. Use synthetic actors and values,
   fake providers/storage, and a fake clock where relevant. A bare schema test
   must not require unrelated business seeding.
4. Community scenarios need authenticated synthetic admin context and owned
   category/handler prerequisites before requests. Notification read scenarios
   must account for the existing sensitive-read audit write; fake/isolated
   persistence must be ready, not bypassed silently as though no audit exists.
5. Only then start the host/send scenarios. No VPS requests, real credentials,
   provider sends, PostgreSQL reset fixtures or Production data writes.

## Acceptance and gates

- Capture focused pre-fix evidence over the generated document for the known
  metadata mismatches. Add regression assertions for **every one of the 28**
  success operations, actual status/content type and resolvable nested refs.
  Include correct array/envelope and nullable/empty-state schemas; merely
  checking that `content` exists or that an attribute is present is inadequate.
- Match representative nonempty nested DTO serialization to generated schemas,
  including wrapper vs plain IDs, numeric enums, nullable values and list
  envelopes. Use MVC's actual serialization options; do not silently use
  different JsonSerializerDefaults. Cover the password-reset response schema
  with synthetic sample values, never a real reset request/temporary password.
- Verify all five required parameter flags in the generated document and their
  existing runtime validation via the isolated host with deterministic dispatch.
- Verify enum values/names against CLR definitions, required wrapper `value`,
  and unchanged serialized runtime examples. No guessed mappings.
- Generated category update body must not require Id. Test missing Id and
  conflicting legacy Id, showing that route Id reaches the handler and the
  unchanged fields are forwarded. Preserve existing unauthorized/forbidden
  behavior using a bounded representative regression, not a new auth redesign.
- Run one final strict `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true`, then focused API metadata/community regression
  tests and existing AdminResponseSchemaContractTests with
  `--no-build --no-restore`. Require exit 0, zero warnings/errors/failures.
  Run architecture 1/1, parse touched Postman JSON, Postman-only contract gate
  (zero missing/orphan), and `git diff --check`. Full integration suite remains
  PORTAL-07; do not launch inherited reset-based database fixtures.

Return root cause, exact base/final SHA, files, fixture/configuration evidence,
pre/post counts, command exits/warnings/skips, the 28-operation schema matrix,
enum/ID sample conventions, compatibility/limitations and any concrete gaps.
Do not claim deployed OpenAPI was fixed: nothing is published/deployed here.
Stop after PORTAL-03; the mastermind reviews before issuing PORTAL-04.
