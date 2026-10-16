# Admin and Care Homes portal API reconciliation

## Frontend follow-up sequence — 2026-10-16

Current checkpoint: **FUR-02 Done — accepted, committed/pushed and deployed**.
FUR-02 [release receipt](reports/fur-02-deployment.md) records deployed revision
`5cd3526`, one Identity migration applied, nine required/applied heads matched,
unchanged VPS configuration, active service and independent tunneled verifier.
Worker and Mastermind [review evidence](reports/fur-02-mastermind-review.md)
remain linked; the exact original frontend OTP exception was not reproduced.
FUR-01's [prior receipt](reports/fur-01-deployment.md) remains valid.
Owner approved the **19-phase full-report roadmap** on 2026-10-16 after its
phase-by-phase explanation: FUR-00 through FUR-10 (11 phases) plus E01–E08
(8 phases). At approval FUR-00 was Done / 18 remained; current closeout is
FUR-00 through FUR-02 Done / **16 remaining**. Approval establishes the roadmap and
sequential workflow, not every detailed API/policy contract. FUR-01 and FUR-02
had separately approved contracts and are now Done. Historical PORTAL phases below
retain their accepted evidence; FUR IDs
are follow-up work, not a reset or renaming of PORTAL-01 through PORTAL-07.

The [new intake audit](frontend-report-audit-2026-10-16.md) covers all 46 supplied
items. [Frontend handoffs](../../../frontend-handoffs/README.md) separates Admin
and App audiences, phase names and dates. It contains owner-forwardable reports,
not a duplicate task tracker. The frontend repositories were not inspected;
contract assumption mismatches are not claimed as proven client code defects.

| Follow-up | Status | Objective and gate |
| --- | --- | --- |
| FUR-00 — Alignment | Done | Report index/template and dated Admin/owner reports prepared; 17 relative file links resolve, all 46 intake IDs referenced across the two reports. Presented for owner review/forwarding, not sent. No external sending implied. |
| FUR-01 — Contract truth (audit S01) | Done | R1–R5 accepted; strict build clean, full unit 3421/0/8, architecture 1/1. [Phase release](reports/fur-01-deployment.md) `f9231fb` deployed: 29/29 metadata operations, nine heads match/zero applied, config hashes unchanged. Public direct-path limitation disclosed. |
| FUR-02 — Verification continuity (S02) | Done | [Approved contract](contracts/fur-02-verification-continuity.md) → [worker delivery](reports/fur-02-worker-delivery.md) → [Mastermind review](reports/fur-02-mastermind-review.md) → [verified phase release](reports/fur-02-deployment.md). Deployed `5cd3526`; Identity 1 migration applied, other eight 0; 9/9 heads and config hashes match. |
| FUR-03 — Finance verification (S03) | Accepted locally — awaiting owner release approval | R1–R5 accepted; no defect reproduced, 12 PostgreSQL proofs reported, no runtime delta, deployment N/A. Mastermind review accepted; scoped commit/push and documentation-only closeout await separate owner approval. See [worker delivery](reports/fur-03-worker-delivery.md) and [Mastermind review](reports/fur-03-mastermind-review.md). |
| FUR-04 — Care Home readback (S04) | Not started | Lock owner current-profile/revision fields, media, resident/owner/dispute/actor display semantics and query-risk coverage. |
| FUR-05 — Owner settlements (S05) | Not started | Lock own-facility read contract reusing existing payout/refund/commission/debt rules. |
| FUR-06 — Eligible caregiver payouts (S06) | Not started | Lock selector contract and reuse full command eligibility. |
| FUR-07 — Community moderation (S07) | Not started | Lock list/detail/reasons/counters and explicitly selected report/block policy; no implicit global suspension grant. |
| FUR-08 — Owner disputes (S08) | Not started | Lock own-facility read/filter/display contract without admin resolution powers. |
| FUR-09 — Caregiver integrity disposition (S09) | Not started | Review dependencies/recovery evidence before any separately approved exact repair/quarantine. Never hide or delete profiles implicitly. |
| FUR-10 — Integration and release handoff | Not started | Final cross-phase integration/closure, audience handoffs and config/migration accounting. Each earlier complete phase already receives Mastermind release verification under the standing workflow; this final gate does not defer those releases. New target/destructive/material decisions still need exact authority. |

Recommended core order: FUR-00 → contract lock → FUR-01 → FUR-02 → FUR-03 →
FUR-04 → FUR-05 → FUR-06 → FUR-07 → FUR-08 → FUR-10. Consider the FUR-09
diagnostic early, but any repair is separate authority. No endpoint feature slice
above is approved merely by inclusion here. Decide the release boundary before
issuing its executable brief; do not wait for every frontend reply to proceed
with independently confirmed, approved work.

### Approved full-report feature roadmap

These eight phase headings are now included in the owner-approved roadmap;
their exact behaviors, permissions and implementation contracts are **not yet
approved**. Their earlier classification as unapproved expansion candidates in
the dated audit remains historical. Resolve only the active phase's decisions.

| Phase | Status | Purpose and contract boundary |
| --- | --- | --- |
| E01 — User statistics and growth | Not started | Agree permitted aggregate counts, entities, date/time buckets and ranges; do not broaden PII access. |
| E02 — Owner legal support | Not started | Agree owner legal audience/content publication and resolve owner ID/deletion questions; do not automatically add personal-ID/deletion workflows. |
| E03 — Medical-access audit | Not started | Define caregiver permission-decision capture, privacy, retention and restricted audit reads. |
| E04 — Platform controls | Not started | Define maintenance/platform-state enforcement, exceptions, public status and supported payment-mode semantics. |
| E05 — Family-home CMS | Not started | Define banners, quick links, featured services and mobile publication. |
| E06 — Missed check-ins | Not started | Define expected population, window/timezone/grace and derived feed. |
| E07 — Caregiver review notes | Not started | Define internal note author, access, history and correction policy. |
| E08 — Landing-media lifecycle | Not started | Protect live/historical references while managing unused media safely. |

Accepted roadmap defaults: gateway secrets stay server-managed, application
configuration registry screens are deferred, Supplier remains postponed. New
requirements for those systems require separate phase/scope approval. Closing
the report includes correct-usage answers and explicit deferrals, not automatic
implementation of every suggestion.

Recommended sequence for the full roadmap: FUR-01 through FUR-08 in order;
review FUR-09 diagnostic early and settle its approved disposition before final
closeout; E01–E08 individually contracted and sequenced afterward; **FUR-10 last**
for the selected release. Smaller releases can be agreed explicitly without
claiming unshipped phases Done. No parallel worker execution is implied.

### Per-slice operating contract

1. Mastermind agrees owner scope, exact observable behavior/DTOs/errors/permissions and exclusions.
2. Pin current main SHA/local delta, owned files and tests in one worker brief.
3. Inspect disposable fixture/config readiness; reuse or seed minimal data, read
   it back, and verify prerequisites before business API testing. Production is
   not disposable; authenticated GETs can write audits. Fake providers by default.
4. Worker implements/tests the entire approved phase and synchronizes its feature
   guides/Postman, not only one subtask; distinguish InMemory logic from actual
   PostgreSQL translation/execution. Keep guides/OpenAPI/Postman aligned. No Bruno.
5. Mastermind performs bounded review; resolve concrete findings and record actual
   commands/counts/skips/warnings/limitations. No broad duplicate re-audit.
6. Add a dated audience report linking original issue → contract → source/test
   evidence → deployment truth. Do not label planned/local work deployed.
7. Worker saves a report and returns it through the owner; Mastermind reviews
   actual source/tests/docs and reports concrete findings and any required
   modifications. The owner gives bounded corrections to the same worker.
   Worker leaves the phase In progress awaiting review, never commits/pushes or
   deploys. Mastermind does not take over feature implementation silently.
8. Owner workflow reaffirmed 2026-10-16: first present one complete phase
   contract and receive explicit approval. Then save the full worker prompt and
   provide a short paste-ready kickoff naming its path and recommended effort;
   the worker reads the full prompt from the repository. Do not paste the full
   file into chat. After complete delivery and
   acceptance, present final evidence and request explicit owner approval for
   commit, push and applicable publish/deploy. Contract approval is not release
   approval. After release approval, Mastermind commits/pushes the complete
   phase, publishes/deploys when applicable, verifies and records the receipt
   before advancing. No per-task or partial-phase release. Mark Done only after
   acceptance plus deployment verification (or explicitly N/A for pure
   research/docs-only work); do not redeploy an unchanged API for docs alone.
9. Phase/release approval does not authorize workers to release, later phase
   contracts, broad data repair, secrets/CORS changes or resets. Exact
   target/revision/config/migration/backup preflight and receipt remain required;
   stop for owner direction on a new target, destructive operation or material
   scope/config decision. No routine branch/worktree under standing guidance.
   Never stage unrelated private files.

FUR-00 acceptance: folder/index/template exist; Admin and Care Home Owner reports
separate correct usage, assumption corrections, backend work and owner decisions;
all 46 intake IDs are accounted for across their union; relative links resolve;
no secrets/code/provider/database changes. Worker roles skipped for this bounded
documentation setup: direct Mastermind execution is sufficient.

FUR-00 evidence (2026-10-16): four audience-framework/report Markdown files;
relative-file-link check 17/17, intake-reference coverage 46/46, whitespace check
and `git diff --check` clean after line-ending normalization. These are document
checks, not runtime tests or independent re-execution of earlier live evidence.
No build/API/provider/database operation needed or run. Existing root intake and
full audit preserved; navigation/history pointers updated without deleting files.

Owner audience correction (2026-10-16): Admin contains `admin-portal` and
`care-home-owner`; App contains Family/Caregiver/Elderly only. The two new
alignment reports were relocated accordingly with their contents/evidence
preserved and links updated. This corrects the initial FUR-00 grouping, not the
API roles or underlying feature contract. An App README records the boundary.

Next action: Mastermind commits/pushes the accepted complete phase, publishes/
deploys to the verified existing target and records verification — **completed**.
Owner next discusses/locks FUR-02's verification-continuity contract before a
new worker prompt. FUR-02 implementation Not started. Roadmap now **2 Done /
17 remaining**; no later contract is silently approved by this closeout.
Reports are prepared for review/forwarding, not sent; frontend
acknowledgement/completion must not be invented. The original audit remains a
dated evidence snapshot, not a competing execution checklist.

## Historical PORTAL delivery and geography evidence

Status: PORTAL-01 through 05 and PORTAL-07 accepted locally (2026-10-15).
PORTAL-06: **Done for owner-accepted V1 scope**. Approved preserve-and-add import
executed: 6081 inserts, final 27/349/5714 including legacy extras; old nine lookup
rows and five selections unchanged, zero orphans, repeat zero. Focused 119/119,
strict build zero warnings/errors; API/lookup verification passes. See the
[executed receipt](reports/geography-import-preserve-add.md). Chronology below
retains earlier decisions; pending-import statements are historical, not active.
Owner authorized the shortest route on 2026-10-15: one fresh disposable local
PostgreSQL rehearsal, preserving existing local databases and the VPS. Rehearsal
Done: actual CLI initial/repeat apply on a fresh PostgreSQL 18.4 cluster yielded
27/347/5712, zero repeat inserts and zero orphans; server stopped. See the
[06B report appendix](reports/phase-06-importer.md). No extra source research,
production reset, commit/push or deployment was performed.
Owner subsequently approved commit/push, publish and deploy. Release preparation
Done: `9167e71` deployed after owner-enabled SSH access; external verifier passes
with startup evidence, 9/9 migration heads match and zero applied. Services,
OpenAPI and governorates HTTP 200; config unchanged, file/config backup retained.
See [deployment receipt](reports/deployment-9167e71.md). Geography import was NOT
run. Lookup reset or replacement remains outside this approval. API runtime config is unchanged;
the standalone importer alone needs temporary SANAD_GEOGRAPHY_CONNECTION.
Owner-approved read-only VPS replacement check is Done: old lookups 4/3/2,
five saved area selections across five caregivers, zero booking Area IDs;
one bilingual mismatch precludes guessed mapping. No data change. See
[replacement impact/owner decision](reports/geography-replacement-preflight.md).
Owner approved the four exact links, including Damanhur grouping. Narrow opt-in
`sanad-vps-v1` importer profile In progress; pins existing IDs/names/parents and
target, preserves legacy selections, requires fresh preview and backed-up apply.
Owner chose preserve-and-add rather than clearing existing selections. In
progress: preserve-and-add data operation. Actual read-only preview Done:
two root spelling conflicts block 944 rows; explicit governorate aliases and
Damanhur grouping need reconciliation. No apply or partial import. Preserve all
existing IDs/names/selections; do not bypass name-conflict guards or silently
perform a partial catalogue import. Any exact reconciliation is reported first.
Audit date: 2026-10-15. Code baseline: pushed `main`
`8cbeedd796ea1d63fab0c4844e664cb409e27fa6`, clean at intake.
The frontend report is input evidence, not an accepted defect list.

## Scope and current environment

Reconcile the supplied Admin/Care Homes portal report against implementation,
published contracts, existing tests and selected anonymous HTTP checks. Reopen
only the evidenced problems and the directly relevant owner-portal test gap.
The owner approved PORTAL-01 through PORTAL-05 and final integration. Execute
one pinned worker brief at a time. Minimal isolated test fixtures are part of
those tasks; production data repair, provider sends and deployment remain outside
this approval.

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

## Execution checklist and acceptance

The sequence is PORTAL-01 -> PORTAL-02 -> PORTAL-03 -> PORTAL-04 -> PORTAL-05
-> final integration. PORTAL-06 waits only on its own dataset decision and
does not block the earlier fixes or minimal owner test fixture. One worker at
a time; prepare the copy-ready brief for each task after the preceding review,
pinning its then-current main SHA, exact owned files and dependencies. Follow
the standing repository branch instruction. Workers preserve others' edits.

- [x] Done — **PORTAL-00: reconcile report and prepare this proposed plan.**
  Source review, selected anonymous live checks and focused existing tests
  completed; verification details below. No authenticated live reproduction.
- [x] Done — **PORTAL-01: prevent reactivation 500 without bypassing verification.**
  Owner approved execution; [worker brief](worker-prompts/01-reactivation.md)
  issued. Worker delivery `4f71493` reviewed and accepted; mastermind completed
  missing partial-verification combinations and strengthened persistence/notice
  assertions. Strict build 0 warnings/errors; focused tests 57/57, Identity model
  7/7, architecture 1/1 and Postman mapping 556/620/0/0. Evidence in
  [phase-01 report](reports/phase-01-reactivation.md).
  Own Identity admin status handler/errors, the AdminUsers controller's status
  mapping, focused domain/handler/controller tests, user-management guide and
  corresponding Postman examples. Accepted rule: missing activation readiness
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
- [x] Done — **PORTAL-02: repair library input contract and field validation.**
  [Worker brief](worker-prompts/02-library-validation.md) issued after Phase 01
  acceptance. Worker delivery `4962053` reviewed and accepted after a bounded
  mastermind correction preserving strict JSON number handling. Added quoted-
  number regressions and literal documented/Postman example tests. Strict build
  0 warnings/errors; focused Library/controller 56/56; architecture 1/1;
  Postman mapping 556/620/0/0. Evidence in
  [phase-02 report](reports/phase-02-library.md).
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
- [x] Done — **PORTAL-03: publish accurate API metadata without a wire-format migration.**
  [Pinned worker brief](worker-prompts/03-api-metadata.md) prepared after
  Phase 02 acceptance. Delivery `57f9d3e` plus correction `13a85ca` reviewed and
  accepted. Mastermind rerun: focused 85/85, mapping 556/620/0/0, diff clean;
  reused valid worker strict build 0 warnings/errors and architecture 1/1.
  Evidence and resolved findings are in
  [phase-03 report](reports/phase-03-api-metadata.md).
  Own the controllers for the 28 listed actions, the five query declarations,
  OpenAPI configuration/transformer files, a dedicated community-update request
  DTO, API contract tests, affected guides/Postman entries. Correction pass
  complete: host isolation proven (jobs removed, all contexts InMemory),
  finance-current empty HTTP proven `204` with matching metadata, nullable-enum
  union verified valid without transformer change. Use actual response
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
- [x] Done — **PORTAL-04: diagnose caregiver identity consistency.**
  [Pinned diagnostic brief](worker-prompts/04-caregiver-integrity.md) executed
  from base `b44fb50` (clean `main == origin/main` at intake). Worker delivery
  `33af090` accepted after bounded mastermind SQL/runbook corrections: read-only diagnostic
  `diagnostics/caregiver-identity.sql` (preflight, list/count reconciliation,
  missing-user listing, account-type classification, per-id dependents,
  split-target Identity/Families counterparts; SELECT-only, read-only
  transaction, short timeout, ROLLBACK, no PII columns) plus operator runbook
  `diagnostics/caregiver-identity-runbook.md`; local regressions
  `tests/Sanad.UnitTests/Caregivers/CaregiverIdentityIntegrityTests.cs` (real
  list/count SQL on an isolated SQLite attached-schema fixture) and
  `CaregiverAdminListIntegrityTests.cs` (diagnostic classification matrix,
  duplicate detection, config fallback/eager-capture) with one bounded helper
  `CaregiverIntegrityFixture.cs`; evidence in
  [phase-04 report](reports/phase-04-caregiver-integrity.md). Strict solution
  build 0 warnings/errors; focused 24/24 (17 new + 7 existing
  admin-review); `git diff --check` clean. No production access/data repair:
  the six reported deployed records remain explicitly unverified pending
  operator evidence; no INNER JOIN hiding, count fix, fabricated names, or
  deletions. Production files untouched. Mastermind rerun 24/24; diagnostic
  listing bounds and UUID extrema corrected, null/blank fallback and partial
  target-mismatch conclusions clarified. PostgreSQL diagnostic execution still
  not run; acceptance does not resolve the deployed six-row finding.
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
- [x] Done — **PORTAL-05: owner-portal endpoint verification with minimal fixtures.**
  [Pinned worker brief](worker-prompts/05-owner-portal-verification.md) executed
  from base `ddab615` (clean `main == origin/main` at intake). Worker delivery
  `6b32906` accepted after mastermind test strengthening. Added `tests/Sanad.UnitTests/API/
  CareHomeOwnerPortalFixture.cs` + `CareHomeOwnerPortalTests.cs` (19/19 green
  over an isolated HTTP host with real handlers/policies; only the Postgres
  advisory-lock visit guard inlined), selected owner/Finance Postman
  descriptions, a `visits.md` numeric-day correction, and evidence in
  [phase-05 report](reports/phase-05-owner-portal.md). Verified: seven owner
  GETs + settings PUT with ownership isolation, populated/empty views,
  Draft/Approved visibility, stale-version 409, range/invalid 400s, and 401/403 matrix;
  no-facility reads 404. One authored-example defect corrected (string
  `"day": "Monday"` rejected with 400; numeric `DayOfWeek` is the wire
  format); no production-code defect found and none changed. Strict build
  0 warnings/errors; mastermind combined 126/126 (19 owner + 107 related);
  mapping 556/620/0/0. Exact ID and Cairo-date assertions strengthened; no
  production-code change. Final integration is next; PORTAL-06 conditional.
- [x] Done — **PORTAL-06: owner-accepted V1 coverage provisioned with approved links; legacy IDs and selections preserved.**
  Owner authorized starting preparation on 2026-10-15. [06A source
  qualification](worker-prompts/06a-geography-source-qualification.md)
  worker delivery `cdf8a2a` reviewed/accepted as research, not dataset approval:
  [qualification report](reports/phase-06-source-qualification.md)
  with [reproducible diagnostics](diagnostics/geography-source-qualification.md).
  Worker proposed pinned `open-admin-data` `76b47b2` levels 1–2 filtered (18
  English-collision pairs + Zemam/police exclusions need per-pair decisions;
  Smouha placement and manual-Areas policy open); `egypt-cities` disqualified
  (no license, untranslated level-3 English); the CAPMAS material inspected in
  06A was not a direct gazetteer (later V1 scan found a supplemental codebook).
  This was research/proposed design
  only; at that stage 06B implementation required source/mapping approval.
  No target-data change was authorized, and no national completeness claimed.
  Mastermind does not recommend that partial import as a solution to the
  three-level picker: Areas remain empty and provenance/locality semantics
  unresolved. Owner explicitly chose **All Egypt**: full three-level nationwide
  coverage, not selected launch regions. Execute
  [06A2 national catalogue preparation](worker-prompts/06a2-national-catalogue.md)
  to assemble/validate the actual sourced bilingual candidate and coverage
  manifest before a 06B importer brief. This does not approve the rejected
  source, change locality semantics or authorize a target write. If data gaps
  remain, report them; no silent reduction to levels 1–2 or manual-Areas-only.
  [06A2 worker delivery](reports/phase-06-national-catalogue.md) `5918813`
  initially held for [bounded corrections](worker-prompts/06a2-review-corrections.md).
  Correction delivery `1b0527b` accepted as correction work after mastermind
  offline reruns (structural exit 0, 12/12 fixtures; readiness exit 2).
  At that review, catalogue acceptance remained blocked on data evidence.
  Owner requested completion on 2026-10-15. Mastermind completed a direct
  OCHA/HDX comparison: every included Arabic name/key matches the attributable
  historical reference, but its ADM3 coverage is explicitly partial and dated
  2017; all included Area English reference values are blank. See
  [data qualification / exact input handoff](reports/phase-06-data-qualification.md).
  Owner-requested inventory: [all 347 City names](reports/egypt-city-names.md)
  and [all 27/347/5712 names with parent hierarchy](reports/egypt-all-location-names.md).
  Derived verbatim from the candidate, not deployed database counts or acceptance.
  Owner subsequently accepted this coverage for V1 subject to a full scan.
  [Full V1 scan](reports/phase-06-v1-scan.md): all 6086 records checked, real
  validators/create/read handlers exercised in 27 isolated governorate fixtures;
  focused suite 90/90, strict build zero warnings/errors. All source Arabic names
  and all unmodified English labels match literally; 18 Markaz suffixes checked.
  Four question-mark labels and 12 delimiter flags are recorded, not silently
  edited. Current exhaustive national certification is no longer the immediate
  V1 scope requirement; legacy strict readiness flags remain honestly unchanged.
  Owner approved the [exact 16-label proposal](reports/phase-06-label-cleanup-proposal.md).
  [Cleanup](reports/phase-06-label-cleanup.md) applied locally with original values
  preserved and all IDs/parents/counts/verification flags unchanged. Owner then
  authorized [06B implementation](worker-prompts/06b-geography-importer.md).
  [Local importer closeout](reports/phase-06-importer.md): standalone CLI,
  exact reviewed preview/hash/target, insert-only transaction, preserved manual
  IDs and inactive state, stale/conflict refusal, mandatory backup/maintenance
  references. Strict build zero warnings/errors; focused 113/113, architecture
  1/1; all 6086 records imported/repeated in SQLite memory. No PostgreSQL
  execution claimed. Next: owner review and explicit commit/push approval;
  no live database write/import/publish/deploy authorized.
  No claim that another wrapper of the same records closes national gaps.
  `reference-data/egypt/candidate.json` corrected to 27/347/5712
  (pre-cleanup SHA-256 `18AB3CB4…C09`; current approved display baseline
  `E048767A…8A8E`), `provenance.md`, `coverage.md` per-governorate
  matrix, new `boundary-checks.json`, hardened offline
  `Validate-Catalogue.ps1` + 12 fixtures (structural 0 failures, 12/12
  checks, exit 0; separate `-Readiness` gate fails closed with exit 2 on
  4 unresolved branches, 5712 provisional areas, unaccepted flags, 5 open
  gaps). Armant/Esna re-parented to Luxor on GOPP evidence; Tiba
  reinstated with its 6 localities (equivalence unresolved); Zemam/port
  exclusions evidenced by child counts; Marina/gaps-count/source-emptiness
  documentation fixed. Areas stay provisional-census; no data write.
  The strict historical completeness findings stay explicit; owner now
  accepts collected nationwide coverage for V1, not a claim of exhaustive current
  geography. Resolve the bounded display findings, then pin an implementation
  brief using that accepted baseline and its provenance. Design a previewable, idempotent import
  that preserves existing IDs/links, detects ambiguous duplicate names under a
  parent, checks hierarchy/active-state consistency, and reports counts/errors.
  Test an initial import and repeat import on a disposable target. Do not embed
  an unreviewed nationwide dataset in a schema migration or enable Development
  seeding on the current VPS. A small owner-test fixture is not national coverage.
  Actual target data changes require an exact import/dataset handoff and backup.
- [x] Done — **PORTAL-07: mastermind integration and frontend handoff.**
  [Pinned final gate brief](worker-prompts/07-final-integration.md) executed
  from base `5dff36c` (clean `main == origin/main` at intake). Worker delivery
  `0561298` accepted after mastermind review. Added `reports/phase-07-integration.md` and
  `frontend-handoff.md`; no production/test code or Postman change was needed.
  Valid safe gates: strict build 0 warnings/errors; full unit 3308 passed,
  0 failed, 8 skipped (5 PostgreSQL integration via documented guards + 3
  pre-declared Families legacy); architecture 1/1; 22 Postman JSONs parse;
  mapping 556/620/0/0; diff-check clean; no new migration or runtime config.
  Mastermind reran full unit/architecture with the new child-process-safe
  `docs/internal/tools/Test-SanadPortalGate.ps1`: 3308/0/8 and architecture 1/1;
  independently checked 22 JSONs, zero mapping gaps and portal Markdown links.
  Frontend handoff wording corrected for unknown parents, sectionsJson and
  generic activation detail. First worker run violated the no-reset instruction
  (reported local test DB resets); excluded from accepted evidence and retained
  in the report. The guarded rerun does not waive that incident.
  External items stay open: deployed caregiver evidence, PORTAL-06 dataset,
  untested nested/mobile routes, media cleanup, Chat runtime/soak gates.

Landing media lifecycle is a separately proposed low-priority follow-up, not
part of the core shipping sequence above. It needs a reference-safe design and
specific approval before introducing deletion or a new cleanup job.

## Fixtures and test execution boundaries

### Mandatory readiness gate before API startup or scenario requests

Every worker brief must include this sequence, and the worker must complete
and record it before launching a test API or sending a scenario request:

1. **Verify the existing configuration.** Identify the exact target, database,
   environment, API port, authentication setup, provider settings and storage
   paths needed by this test. Reuse correct existing configuration; do not
   guess credentials, inherit an unchecked database target, or silently change
   Production to Development. Keep secret values out of reports.
2. **Inspect existing seeded data first.** Read back the required actors,
   roles, ownership, IDs, relationships and lifecycle states. A record's
   existence alone is not sufficient: it must meet the scenario preconditions.
   Reuse suitable data rather than replay onboarding or duplicate fixtures.
3. **Seed only what is missing before starting the API.** On the designated
   verified disposable target, use an existing guarded seed/test helper or add
   the smallest bounded fixture helper. Prepare required prerequisite records
   without launching the API or relying on startup seeding/migrations. Requests
   whose purpose is to test creation can create their own records later; their
   actors and other prerequisites must already be ready.
4. **Verify the prepared state.** Read back the fixtures and required
   relationships/statuses; confirm fake providers and test storage are ready.
   Record a concise readiness manifest with test actors, fixture IDs/states,
   configuration key names and reuse/creation counts. No passwords, tokens,
   connection strings or real users' data belong in that manifest.
5. **Then start the API and execute the selected requests.** Match requests to
   the readiness manifest. If a scenario consumes or changes fixture state,
   recheck it before reuse or prepare another minimal fixture first. A test
   must not fail merely because its prerequisites were never prepared.

For an isolated OpenAPI/fake-handler test host or an anonymous metadata probe
that requires no database fixtures, explicitly mark fixtures not required and
prepare the corresponding fake/configuration setup first; do not seed unrelated
records just to satisfy a checklist. This readiness gate does not authorize
seeding or resetting the existing Production VPS/database. When configuration
or safe fixture access is missing, report the specific dependency and continue
independent checks that do not depend on it.

| Test area | Reuse first | Smallest additional fixture if absent |
| --- | --- | --- |
| Reactivation | Existing isolated Identity handler test contexts | Acting SuperAdmin; target users with verified/unverified contacts, Suspended and Blocked states; explicit sessions; recording notice publisher |
| Library | `tests/Sanad.UnitTests/Cms/LibraryTests.cs` data helpers | Draft Article and Video, valid tiny image, category, fake storage; invalid multipart requests do not need permanent records |
| OpenAPI/JSON | Existing API test-host/fake sender pattern | Representative DTOs returned by a fake handler; generated OpenAPI through HTTP; no real database or provider startup |
| Caregiver consistency | Existing valid isolated caregiver + Identity user | One deliberately missing Identity user in a disposable test only; compare list/count and diagnostic output |
| Owner portal | Existing owner/Care Homes lifecycle helpers | Two owner identities, one owned approved facility plus draft state, minimal location chain, inventory, one appropriate booking/visit fixture per populated view |
| Geography import | Existing approved location rows | Small bilingual parent/child dataset with repeat, duplicate and invalid-parent cases |

Read fixture preconditions first and record IDs privately in the run context.
Prepare prerequisites with guarded local fixture helpers before API startup;
the supported APIs under test run only after the readiness gate. Avoid broad
onboarding replay when a valid state can be reused. Do not run reset-based PostgreSQL test
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

## Approved decisions — 2026-10-15

The owner approved PORTAL-01 through PORTAL-05 and final integration with the behavior above:
409 for missing activation readiness; accept both library JSON casing styles;
preserve current enum/ID wire formats; keep existing first-run 404 and audience
403; diagnose caregiver integrity before repair. PORTAL-06 remains a separate
data-provisioning task until an approved dataset/scope exists. Optional landing
media cleanup is deferred. The mastermind supplies sequential, pinned worker
prompts. PORTAL-01 through 05 and local PORTAL-07 are accepted. PORTAL-06 still
requires approved source/coverage; deployment/import and remaining runtime/data
evidence are separate owner decisions, not automatic next actions.
On 2026-10-15 the owner authorized beginning Phase 06 preparation. A bounded
read-only 06A source-qualification brief is issued; its report must return for
dataset/coverage/mapping approval before a 06B implementation brief.
The owner subsequently selected **All Egypt**. Coverage choice is resolved:
national Governorate/City/Area catalogue preparation is issued as 06A2. Actual
dataset/mapping acceptance, importer implementation and live application remain
separate gates; no Production write or deployment is authorized.
Subsequent owner decisions accepted the available V1 coverage, approved the
exact 16-label cleanup, and authorized local 06B implementation/tests. These
supersede the earlier preparation-only restrictions, not the separate release,
live target/preview/import and deployment approval gates.
