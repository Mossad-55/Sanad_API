# Admin and Care Homes portal API reconciliation

Status: review complete; proposed implementation awaits owner approval.
Audit date: 2026-10-15. Code baseline: pushed `main`
`8cbeedd796ea1d63fab0c4844e664cb409e27fa6`, clean at intake.
The frontend report is input evidence, not an accepted defect list.

## Scope and current environment

Reconcile the supplied Admin/Care Homes portal report against implementation,
published contracts, existing tests and selected anonymous HTTP checks. Reopen
only the evidenced problems and the directly relevant owner-portal test gap.
No implementation, database repair, provider send, deployment or fixture write
is authorized by this proposed plan alone.

The owner clarified that the existing VPS runs **Production** and is currently
used for production-mode developer testing; it is not a disposable Staging
database. A separate hardened VPS comes later. The frontend report's label
"dev" does not change that boundary. The deployed revision was verified as
the audit baseline and `databaseReachable` was true. Owner-confirmed CORS stays
unchanged. Bruno is excluded.

Authenticated live findings in the frontend report are developer-reported,
not independently repeated here. This audit used anonymous GETs for deployment
info, OpenAPI and location lookups. No token or personal data was needed.

## Finding-by-finding verdict

| Report item | Verdict | Evidence and proposed disposition |
| --- | --- | --- |
| A1 reactivation 500 | Confirmed unhandled failure path in code; developer observed HTTP 500 | `ReactivateAdminUserCommandHandler` calls `User.Reactivate`, which calls `ValidateActivationReadiness`. A freshly admin-created user has unverified phone/email, so reactivation throws `DomainException`; the handler does not translate it and the registered exception handler handles only FluentValidation. Existing handler tests use previously Active/verified users. This does **not** establish that every verified user fails. Fix as PORTAL-01. |
| A2 six caregiver Identity mismatches | Data inconsistency reported; query behavior confirmed; cause unproven | `CaregiversDbContext.GetAdminCaregiversAsync` deliberately LEFT JOINs `identity.users` and returns nullable name/phone fields; the count includes all caregivers. There is a join, not a missing enrichment call. Full IDs, existence, actual module database targets and reference dependencies must be checked before repair. Do not switch only the list to INNER JOIN or delete rows to hide the symptom. PORTAL-04 is diagnostic first. |
| B3 optional query parameters | Confirmed specification mismatch | Live OpenAPI marks all five parent/date query parameters optional. Source requires non-default parent IDs/date. Preserve these business requirements and declare them required. The additional claim that city/area 400s lack field details was **not reproduced**: both anonymous responses contain `errors.GovernorateId` / `errors.CityId`, `code: Api.Validation.Failed`, and a trace ID. |
| B4 library request rules | Parser/docs mismatch and poor validation feedback confirmed; claim that rules are nowhere documented is partly incorrect | `TryParseSections` uses default case-sensitive `JsonSerializer.Deserialize`; Postman and `docs/app/library/README.md` show camelCase. That guide already documents positive unique display order, at least one Article section and positive Video duration. Domain handlers reduce expected validation failures to `Cms.LibraryContent.Invalid`, then the mapper hides the reason behind a generic detail. Accept both casing styles and return safe field errors while retaining the rules. PORTAL-02. |
| B5 28 response schemas | Confirmed in deployed OpenAPI | The 22 GETs and six actions below have no published success payload schema. The actual notification path parameter is `{notificationId}`. Additionally, create-admin is published as 200 although its controller returns 201. PORTAL-03 must correct status and schema together. |
| B6 enum names | Confirmed contract metadata gap | Live `UserStatus`, `AccountType`, `DevicePlatform`, `CaregiverType` schemas are bare integers. Audit action has no usable component while its response schema is absent. `AdminUserActionType` in source confirms Reactivated=2; no guessing is needed. Publish numeric values plus names without changing JSON serialization. |
| B5b location reference data | Environment coverage shortage confirmed; nationwide source/scope outstanding | Anonymous readback: Alexandria 0 cities/0 areas; Asyut 0/0; Beheira 1/1; Minya 2/1. Total 4 governorates/3 cities/2 areas. Current Care Homes `CareHomeProfileDraft` accepts location **strings**, not these lookup IDs; the picker dependency is a frontend constraint, not an enforced backend relationship. Existing test seeding is small fixture data, not an authoritative national catalogue. PORTAL-05 can use minimal fixtures now; PORTAL-06 requires an approved dataset. |
| C7 enum filter/response convention | Supported behavior, documentation improvement | `AdminQueryParsing.TryEnum` accepts case-insensitive names **and defined numeric strings**. It is not name-only. Numeric JSON enum responses are current behavior; chat report strings are a separate established contract. Preserve both rather than globally switch enum converters. Document per-resource input/output representations. |
| C8 wrapped versus string IDs | Confirmed existing contract inconsistency, not an immediate wire-format migration | Lookups expose typed record structs such as `GovernorateId` as `{value: guid}`; users and other DTOs flatten Guid IDs. Live wrapper schemas fail to require `value`. Publish actual shapes and require the existing value in wrapper schemas. Do not install a global flattening converter or change existing response types in this patch. |
| C9 support contact 404 | Intended and documented | `docs/admin/help-center.md` explicitly defines 404 `Cms.Help.SupportContactNotFound` before first configuration; handler returns that code. Keep the portal's existing "not configured" state. No change to 204 or an empty success object is proposed. |
| D landing media retention | Real lifecycle limitation; separate low-priority design | Upload saves a generated storage key. No independent deletion/expiry ledger is present. Failed-upload cleanup is not cleanup of successfully uploaded but unused media. A future cleanup must protect references in draft/published content and reused keys; do not add blind DELETE or age-based filesystem removal. |
| D community update body ID | Confirmed contract mismatch | Actual route is `PUT /api/v1/admin/community/categories/{categoryId}`. Body is `UpdateCategoryCommand` with required `Id`, but controller overwrites it from the route. Use a dedicated body DTO without Id and keep the route authoritative; legacy bodies containing an ignored Id must remain accepted. |
| D admin calls to app Help/Legal | Intended | `ApiControllerBase.TryGetCmsAudienceFromClaims` deliberately excludes admin roles. Existing 403 UnsupportedAudience is consistent with the app-audience contract; use the Admin management routes. |
| D smoke leftovers | Reported fixture residue, not this audit's creations | One anonymized/blocked admin, five archived library items and one landing image are reported. No exact IDs or storage key were supplied. Do not clean them automatically or use their truncated names as deletion criteria. |

Additional library validation risk found in the same parser: `sectionsJson=[null]`
deserializes a list containing a null entry and then dereferences `item.DisplayOrder`
outside the `JsonException` catch. This is a source-confirmed unhandled path,
not an independently executed production POST. Include it in PORTAL-02's
local failing regression before changing behavior.

## Source map

Paths below are relative to the repository root; these are current evidence,
not permission to modify every listed file in one worker task.

- Reactivation/create/rules: `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatusCommands.cs`,
  `AdminUserAccountCommands.cs` in that directory, and
  `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/User.cs`.
  Existing accepted rule: `docs/internal/work/admin-users/plan.md` allows
  activation only after normal verification; admins cannot force-verify.
- API result mapping: `src/API/Sanad.API/Controllers/AdminUsersController.cs`,
  `ProblemDetail/ValidationExceptionHandler.cs`, `ProblemDetail/ResultProblemDetailsMapper.cs`.
- Caregiver join/count: `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/CaregiversDbContext.cs`.
  List DTO/query: `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Onboarding/AdminCaregiverReviews.cs`.
- Library parser: `src/API/Sanad.API/Controllers/AdminLibraryController.cs`.
  Validation: `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryContentCommands.cs`,
  `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Library/LibraryContent.cs` and `LibraryContentSection.cs`.
  Contracts: `docs/app/library/README.md`, `docs/postman/admins/Sanad.Admin.postman_collection.json`.
- Required queries: `LookupsController.cs`, `AdminLookupsController.cs`,
  `AdminCareHomesController.cs` under `src/API/Sanad.API/Controllers/`;
  `CityLookups.cs` / `AreaLookups.cs` in Caregivers Application Lookups and
  `AdminCareHomeLicenseExpiries.cs` in CareHomes Application Facilities.
- OpenAPI registration: `src/API/Sanad.API/DependencyInjection.cs` currently uses `services.AddOpenApi()`.
  Existing metadata tests: `tests/Sanad.UnitTests/API/AdminResponseSchemaContractTests.cs`
  cover a different bounded subset, not the 28 reported actions.
- IDs/enums: `src/BuildingBlocks/Domain/Sanad.BuildingBlocks.Domain/Primitives/Ids/`,
  Identity Domain Users (`UserStatus`, `AccountType`, `AdminUserActionType`),
  Identity Domain Authentication/DeviceSessions (`DevicePlatform`), Caregivers Domain (`CaregiverType`).
- Location model: `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeProfileRevision.cs`.
  Limited local fixture provisioning: `src/API/Sanad.API/Seeding/TestUserDataSeeder.cs`.
- Other dispositions: `AdminCommunityCategoriesController.cs`, `AdminHelpCenterController.cs`,
  `AdminLandingController.cs`, `ApiControllerBase.cs`; `docs/admin/help-center.md`.

## Required success-schema inventory (PORTAL-03)

All paths below start with `/api/v1`. Declare the actual handler DTO/envelope,
including nullable fields, nested records, IDs and actual status. Do not
document a `Result<T>` wrapper when the controller returns its Value.

| Method | Path | Success |
| --- | --- | --- |
| GET | `/admin/users` | 200 |
| GET | `/admin/users/{userId}` | 200 |
| GET | `/admin/users/sessions` | 200 |
| GET | `/admin/users/audit` | 200 |
| GET | `/admin/elderly/check-ins` | 200 |
| GET | `/admin/elderly/check-ins/aggregate` | 200 |
| GET | `/admin/elderly/help-requests` | 200 |
| GET | `/admin/elderly/help-requests/aggregate` | 200 |
| GET | `/admin/elderly/medications` | 200 |
| GET | `/admin/elderly/medications/adherence` | 200 |
| GET | `/admin/elderly/sos` | 200 |
| GET | `/admin/care-homes/check-in-disputes` | 200 |
| GET | `/admin/cms/elderly-welcome` | 200 |
| GET | `/admin/finance/platform-charge-rules/current` | 200 |
| GET | `/admin/finance/platform-charge-rules/history` | 200 |
| GET | `/admin/notifications` | 200 |
| GET | `/admin/notifications/{notificationId}` | 200 |
| GET | `/admin/notifications/timeline` | 200 |
| GET | `/admin/notifications/aggregate` | 200 |
| GET | `/admin/sentence-builder/catalog` | 200 |
| GET | `/admin/wellness-tips` | 200 |
| GET | `/care-homes/inventory/admin/{facilityId}` | 200 |
| POST | `/admin/users` | 201 |
| POST | `/admin/users/{userId}/suspend` | 200 |
| POST | `/admin/users/{userId}/block` | 200 |
| POST | `/admin/users/{userId}/reactivate` | 200 |
| POST | `/admin/users/{userId}/password-reset` | 200 |
| PUT | `/admin/users/{userId}/account-type` | 200 |

Mark `through` required on license-expirations and `governorateId` / `cityId`
required on both Admin and public location endpoints (five operations).
Validate omitted, empty, malformed and nonexistent IDs separately; a supplied
unknown parent need not have the same outcome as an omitted required parent.

## Proposed execution checklist and acceptance

The sequence is PORTAL-01 -> PORTAL-02 -> PORTAL-03 -> PORTAL-04 -> PORTAL-05
-> final integration. PORTAL-06 waits only on its own dataset decision and
does not block the earlier fixes or minimal owner test fixture. One worker at
a time; prepare the copy-ready brief for each task after owner plan approval,
pinning its then-current main SHA, exact owned files and dependencies. Follow
the standing repository branch instruction. Workers preserve others' edits.

- [x] Done — **PORTAL-00: reconcile report and prepare this proposed plan.**
  Source review, selected anonymous live checks and focused existing tests
  completed; verification details below. No authenticated live reproduction.
- [ ] Not started — **PORTAL-01: prevent reactivation 500 without bypassing verification.**
  Own Identity admin status handler/errors, the AdminUsers controller's status
  mapping, focused domain/handler/controller tests, user-management guide and
  corresponding Postman examples. Proposed rule: missing activation readiness
  produces 409 `Identity.Admin.ActivationNotReady` with a useful safe message;
  keep the target status unchanged, create no success audit/notice, restore no
  sessions. Verified Suspended/Blocked users still reactivate to Active with
  200 and one audit/notice. Pending users must complete normal OTP verification;
  do not mark contacts verified or force Active. Existing verification handlers
  can verify channels while suspended/blocked; the status action follows that
  verification. Preserve seeded-user protection and existing invalid-transition
  codes. Read actual domain verification readiness, not the Admin DTO's
  status-derived `IsVerified` flag (which is not proof of verified contacts).
  Cover never-verified and partly-verified targets, both source statuses,
  verified controls, missing user, seeded target, repeat calls and audit/session
  side effects with deterministic fixtures.
- [ ] Not started — **PORTAL-02: repair library input contract and field validation.**
  Own AdminLibrary parser/request-validation helpers, bounded Library command/
  domain changes only if needed, Library tests, guide and Admin Postman Library
  folder. Accept existing PascalCase and documented camelCase section keys;
  keep Article/Video business rules, Draft lifecycle and existing successful
  payloads. Validate malformed JSON, null list entries, missing/zero/duplicate
  display orders, empty Article sections, title/summary/section/bullet limits,
  Video URL/duration and forbidden mixed fields. Return a safe `errors` map
  identifying fields/indexes and a trace ID; preserve existing `Cms.Library*`
  top-level codes for established failure cases. Keep this validation local to
  Library rather than exposing raw DomainException messages globally. On update,
  omitted sections keep existing sections; explicit `[]` is not the same as
  omission. Empty bullet arrays remain valid. Reject `[null]` as 400. Rejected
  requests must not change records or leave newly uploaded covers; preserve the
  old cover when an update fails. Examples must actually be exercised locally.
- [ ] Not started — **PORTAL-03: publish accurate API metadata without a wire-format migration.**
  Own the controllers for the 28 listed actions, the five query declarations,
  OpenAPI configuration/transformer files, a dedicated community-update request
  DTO, API contract tests, affected guides/Postman entries. Use actual response
  types/statuses, including existing empty/null-state 204 or not-found outcomes
  where applicable, rather than assuming every successful read has a body.
  Publish numeric enum values plus names from their CLR enum
  definitions, and describe string enum filters. Make wrapper `value` required
  in its schema while preserving wrapper/string shapes already returned by each
  route. Remove redundant category Id from the required request body through a
  separate request DTO; route remains authoritative, including conflicting
  legacy body Ids. Add tests over the **generated OpenAPI document** and sample
  serialized payloads; reflection-only attribute tests are insufficient. Include
  representative nonempty nested data, nullable cases and password-reset schema
  without real secret values. Preserve 404 unset contact and app-audience 403.
- [ ] Not started — **PORTAL-04: diagnose caregiver identity consistency.**
  Own a bounded read-only diagnostic/report and local list/count/integrity tests.
  Obtain full IDs via the authorized list or exact local fixture, check the
  actual Identity and Caregivers targets without outputting connection secrets,
  compare missing users/roles and dependent bookings/documents/finance references.
  Reproduce missing-Identity behavior in a disposable fixture. Separate stale
  fixture data from an evidenced write-path defect. Recommend repair/quarantine
  or an explicit admin integrity indicator only after evidence. No fabricated
  names, re-created identities, silently filtered rows, or production deletion.
  The isolated missing-user fixture proves query behavior only, not the cause
  of the six reported deployed records. If deployed read access is unavailable,
  deliver the bounded diagnostic query and an operator handoff and leave that
  conclusion explicitly unverified; independent fixes/testing can continue.
  Any necessary product/data repair becomes its own exact reviewed change.
- [ ] Not started — **PORTAL-05: owner-portal endpoint verification with minimal fixtures.**
  Own a targeted local API/fixture test harness, existing Care Homes test helpers
  as needed, Postman owner examples and a concise results report. Reuse a valid
  owner/facility/location/inventory fixture after checking its lifecycle. If
  absent, create only the actors/states needed by the scenarios below. Verify
  facilities/mine, inventory/mine, booking-requests/mine, visits/mine,
  dashboard/mine, revenue/mine and existing visit-settings route(s), deriving
  exact routes/contracts from current controllers. Check an owned and a foreign
  facility, version handling where relevant, SuperAdmin/foreign-owner denial,
  draft versus approved facility state, and empty versus populated booking/visit/
  revenue views using existing provider-free booking fixtures. Record returned
  shapes and expected 200/204/404 by state. No fabricated "all owner endpoints
  healthy" claim from an admin 403, nor from empty lists alone. Report uncovered
  detail routes explicitly; unrelated mobile endpoints are outside this task.
- [ ] Not started — **PORTAL-06: reference-data provisioning, pending approved source.**
  Propose an authoritative bilingual hierarchy/dataset with provenance and scope
  (what counts as a city and an area). Design a previewable, idempotent import
  that preserves existing IDs/links, detects ambiguous duplicate names under a
  parent, checks hierarchy/active-state consistency, and reports counts/errors.
  Test an initial import and repeat import on a disposable target. Do not embed
  an unreviewed nationwide dataset in a schema migration or enable Development
  seeding on the current VPS. A small owner-test fixture is not national coverage.
  Actual target data changes require an exact import/dataset handoff and backup.
- [ ] Not started — **PORTAL-07: mastermind integration and frontend handoff.**
  One bounded independent review after implementation; correct concrete findings,
  strict zero-warning build and focused tests per change, full unit/architecture
  once for the integrated cross-cutting metadata change, Postman JSON parsing,
  contract-map zero missing/orphans and diff-check. Document changed codes,
  schema examples and frontend adaptations; publish only on an owner order.
  Deployed verification after owner deployment starts with anonymous revision/
  migration/smoke checks. Authenticated tests use a secure route (HTTPS or an
  approved SSH tunnel) and designated test actors. Preserve the separate open
  Chat Plus runtime/device/soak gates; this work does not close Prompt 7 by name.

Landing media lifecycle is a separately proposed low-priority follow-up, not
part of the core shipping sequence above. It needs a reference-safe design and
specific approval before introducing deletion or a new cleanup job.

## Fixtures and test execution boundaries

| Test area | Reuse first | Smallest additional fixture if absent |
| --- | --- | --- |
| Reactivation | Existing isolated Identity handler test contexts | Acting SuperAdmin; target users with verified/unverified contacts, Suspended and Blocked states; explicit sessions; recording notice publisher |
| Library | `tests/Sanad.UnitTests/Cms/LibraryTests.cs` data helpers | Draft Article and Video, valid tiny image, category, fake storage; invalid multipart requests do not need permanent records |
| OpenAPI/JSON | Existing API test-host/fake sender pattern | Representative DTOs returned by a fake handler; generated OpenAPI through HTTP; no real database or provider startup |
| Caregiver consistency | Existing valid isolated caregiver + Identity user | One deliberately missing Identity user in a disposable test only; compare list/count and diagnostic output |
| Owner portal | Existing owner/Care Homes lifecycle helpers | Two owner identities, one owned approved facility plus draft state, minimal location chain, inventory, one appropriate booking/visit fixture per populated view |
| Geography import | Existing approved location rows | Small bilingual parent/child dataset with repeat, duplicate and invalid-parent cases |

Read fixture preconditions first and record IDs privately in the run context.
Use supported APIs or existing guarded local fixture helpers; no broad onboarding
replay when a valid state can be reused. Do not run reset-based PostgreSQL test
fixtures against inherited connection strings. Verify any PostgreSQL target is
disposable and authorization covers the intended writes; no database resets are
part of these tasks. Use fake providers for local tests, never real OTP/email/
payment/push sends. Some admin GETs intentionally write operational audit rows,
so HTTP GET alone does not mean a zero-write test. No automatic cleanup of the
frontend's reported residual records or uploaded file is included.

## Executed audit evidence

- `git status --short --branch`: clean `main...origin/main`; code baseline above.
- Anonymous remote GET `/api/v1/deployment/info`: HTTP success; revision matches
  baseline; environment Production; databases reported reachable.
- Repository `Verify-SanadDeployment.ps1` executed with the current local
  release manifest, the owner-provided base URL and expected revision above:
  exit 0, deployed revision match, required/applied migrations **9/9 match**,
  service lookup HTTP 200. No startup log was supplied, so startup-log evidence
  was not checked by that invocation.
- Anonymous GET `/openapi/v1.json`: verified missing success schemas for all 28
  listed operations, optional flags for five required parameters, bare integer
  enum components, wrapper IDs lacking required `value`, community update Id
  marked required despite route overwrite, and create-admin default 200 metadata.
- Anonymous location GETs: four governorates and parent-scoped city/area counts
  as listed above. Requests without parent IDs returned HTTP 400 with field
  errors and trace IDs. One initial audit-script lookup accidentally combined
  array IDs and got the expected malformed-Guid 400; its count result was
  discarded, the script's array handling corrected, and per-parent counts rerun.
- Existing focused tests executed with `--no-build --no-restore --nologo -v minimal`
  on `tests/Sanad.UnitTests/Sanad.UnitTests.csproj`, filter:
  `FullyQualifiedName~Sanad.UnitTests.Identity.AdminUserManagementTests|FullyQualifiedName~Sanad.UnitTests.Identity.Users.UserStatusTransitionTests|FullyQualifiedName~Sanad.UnitTests.Cms.LibraryTests|FullyQualifiedName~Sanad.UnitTests.Cms.HelpCenterTests|FullyQualifiedName~Sanad.UnitTests.Caregivers.Lookups`.
  Exit 0; **257 passed, 0 failed, 0 skipped**, no warnings emitted. These are
  existing tests, not new regression coverage for the missing cases. No API
  startup, PostgreSQL integration fixture or provider was invoked by this filter.
- No code build was needed for the documentation-only audit. Product files are
  unchanged. Frontend's authenticated read/write observations remain attributed
  to that developer; no latency guarantee is inferred from its sample.

## Decisions proposed for approval

Approve PORTAL-01 through PORTAL-05 and final integration with the behavior above:
409 for missing activation readiness; accept both library JSON casing styles;
preserve current enum/ID wire formats; keep existing first-run 404 and audience
403; diagnose caregiver integrity before repair. PORTAL-06 remains a separate
data-provisioning task until an approved dataset/scope exists. Optional landing
media cleanup is deferred. After approval, the mastermind supplies sequential,
pinned worker prompts; no worker implementation starts from this draft alone.
